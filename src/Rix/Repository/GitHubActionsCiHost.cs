using System.Text;
using System.Text.Json.Serialization;

namespace Rix.Repository;

/// <summary>Reads a GitHub Actions run: whether it failed, and what its failing jobs logged. The
/// GitHub Actions implementation of <see cref="ICiHost"/> — every Actions-specific thing about a
/// run, from the REST paths to the field names to the word "conclusion", stops here.
///
/// Shares its <see cref="GitHubApi"/> transport with <see cref="GitHubCiFailureRepoHost"/>, because
/// on GitHub the CI provider and the repo host happen to be the same service behind the same
/// credential. That is a fact about GitHub, not about the seam: <see cref="Startup"/> hands both the
/// same instance, and a setup where they weren't the same service would hand them different
/// ones.</summary>
internal sealed class GitHubActionsCiHost : ICiHost
{
    private readonly GitHubApi _api;

    /// <summary>GitHub's maximum page size for the jobs endpoint, so a run's jobs are walked in as
    /// few round trips as the API allows. Also the signal that ends the walk: a page holding fewer
    /// than this many jobs is the last one.</summary>
    private const int JobsPageSize = 100;

    /// <summary>How much of a job log is held at once while streaming it. Only ever this much on top
    /// of the tail being kept, no matter how large the log is.</summary>
    private const int LogChunkChars = 8192;

    internal GitHubActionsCiHost(GitHubApi api) => _api = api;

    internal GitHubActionsCiHost(RepoIdentifier repo, GitReadToken token, HttpMessageHandler? handler = null)
        : this(new GitHubApi(repo, token, handler)) { }

    /// <summary>Fetches a run's conclusion, title, URL, head branch and head repo — the facts
    /// needed to decide whether it failed, whether it may be answered at all, and to describe the
    /// failure. <c>conclusion</c> is the one field GitHub itself sends as <c>null</c> (while the run
    /// is still queued/in-progress), so it's the one field this doesn't require; a run whose head
    /// repo is missing (GitHub omits it once a fork has been deleted) is rejected here rather than
    /// defaulted, since the caller decides by comparing it and has no safe value to compare.</summary>
    public async Task<WorkflowRun> GetRunAsync(RunId runId, CancellationToken cancellationToken)
    {
        var operation = $"get workflow run {runId.Value}";
        try
        {
            var run = await _api.GetJsonAsync($"actions/runs/{runId.Value}", GitHubActionsApiJsonContext.Default.WorkflowRunApiResponse, operation, cancellationToken);
            if (run.DisplayTitle is null || run.HtmlUrl is null || run.HeadBranch is null || run.HeadRepository?.FullName is null)
                throw new CiHostException($"{operation} response was missing a required field");
            return new WorkflowRun(run.Conclusion, run.DisplayTitle, run.HtmlUrl, run.HeadBranch, run.HeadRepository.FullName);
        }
        catch (RepoHostException ex)
        {
            throw AsCiFailure(ex);
        }
    }

    /// <summary>Fetches the logs of every job that failed in the run and hands them to
    /// <see cref="JobLogExcerpt"/>, which owns how they're laid out and how
    /// <paramref name="totalTailChars"/> is divided. Logs are fetched concurrently since each is
    /// independent. Relies on .NET's default redirect handling, which strips the
    /// <c>Authorization</c> header when a redirect crosses to a different host — this endpoint always
    /// 302s to short-lived, pre-signed blob storage URLs that reject an unexpected auth header, so the
    /// token must not follow.</summary>
    public async Task<string> GetFailedJobLogsAsync(RunId runId, int totalTailChars, CancellationToken cancellationToken)
    {
        try
        {
            var failedJobs = await ListFailedJobsAsync(runId, cancellationToken);
            var included = failedJobs.Take(JobLogExcerpt.MaxJobs).ToList();
            if (included.Count == 0)
                return "";

            var tailCharsPerJob = JobLogExcerpt.TailCharsPerJob(totalTailChars, included.Count);
            var logs = await Task.WhenAll(included.Select(job => GetJobLogAsync(job.Id, tailCharsPerJob, cancellationToken)));
            return JobLogExcerpt.Render(included, logs, failedJobs.Count - included.Count);
        }
        catch (RepoHostException ex)
        {
            throw AsCiFailure(ex);
        }
    }

    /// <summary>Restates a failure of the shared GitHub transport as this host's own. That transport
    /// is older than the CI/repo split and still reports in <see cref="RepoHostException"/>'s terms,
    /// so translating at the two entry points keeps the seam's contract honest without every caller
    /// of <see cref="GitHubApi"/> having to know which side of the split it is being used from. The
    /// message already names the operation, so it passes through unchanged; the original is kept as
    /// the inner exception.</summary>
    private static CiHostException AsCiFailure(RepoHostException ex)
    => new(ex.Message, ex);

    /// <summary>Collects the run's failed jobs across every page of the jobs endpoint, which is
    /// paginated. A matrix build can easily exceed one page, and the job that failed is no more
    /// likely to be on the first page than the last, so stopping there would silently produce an
    /// empty or partial log excerpt for exactly the runs this command exists to explain. Pages are
    /// read sequentially rather than concurrently because how many there are isn't known until a
    /// short page ends the walk. A job GitHub reports without a name falls back to its ID, which is
    /// still enough to tell one block of the excerpt from another.</summary>
    private async Task<List<CiJob>> ListFailedJobsAsync(RunId runId, CancellationToken cancellationToken)
    {
        var operation = $"list jobs for run {runId.Value}";
        var failedJobs = new List<CiJob>();
        var page = 1;
        while (true)
        {
            var jobs = await _api.GetJsonAsync
            (
                $"actions/runs/{runId.Value}/jobs?per_page={JobsPageSize}&page={page}",
                GitHubActionsApiJsonContext.Default.WorkflowJobsApiResponse,
                operation,
                cancellationToken
            );
            if (jobs.Jobs is null)
                throw new CiHostException($"{operation} response was missing the jobs field");

            failedJobs.AddRange
            (
                jobs.Jobs
                    .Where(job => job.Conclusion == "failure")
                    .Select(job => new CiJob(job.Id, job.Name ?? $"job {job.Id}"))
            );
            if (jobs.Jobs.Count < JobsPageSize)
                return failedJobs;

            page++;
        }
    }

    /// <summary>Keeps only the last <paramref name="tailChars"/> characters of the job's log. A
    /// flooding CI job can emit tens of MB, and only this job's share of the excerpt's budget can
    /// ever be shown, so the rest is never worth holding. The response is read headers-first and
    /// consumed as a stream, with each chunk dropped once it falls out of the tail window, which
    /// bounds peak memory across all the jobs at roughly the caller's whole budget; letting the
    /// response buffer itself and slicing the resulting string would materialize every full log
    /// first and make the cap purely cosmetic.</summary>
    private async Task<string> GetJobLogAsync(long jobId, int tailChars, CancellationToken cancellationToken)
    {
        var operation = $"get logs for job {jobId}";
        using var logResponse = await _api.GetAsync($"actions/jobs/{jobId}/logs", HttpCompletionOption.ResponseHeadersRead, operation, cancellationToken);
        GitHubApi.EnsureSuccess(logResponse, operation);
        return await GitHubApi.TransportAsync
        (
            () => ReadLogTailAsync(logResponse, tailChars, cancellationToken), operation, cancellationToken
        );
    }

    /// <summary>Streams the response body, keeping only its last <paramref name="tailChars"/>
    /// characters. Split out of <see cref="GetJobLogAsync"/> so the entire read — opening the stream
    /// and every chunk after it — sits inside one <see cref="GitHubApi.TransportAsync"/> call, rather
    /// than paying for a wrapper per chunk.</summary>
    private static async Task<string> ReadLogTailAsync
    (
        HttpResponseMessage response, int tailChars, CancellationToken cancellationToken
    )
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
        var tail = new StringBuilder();
        var buffer = new char[LogChunkChars];
        while (true)
        {
            var read = await reader.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return tail.ToString();

            tail.Append(buffer, 0, read);
            if (tail.Length > tailChars)
                tail.Remove(0, tail.Length - tailChars);
        }
    }
}

/// <summary>The JSON body of a GitHub "get a workflow run" REST response.</summary>
internal sealed record WorkflowRunApiResponse
(
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("display_title")] string? DisplayTitle,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("head_branch")] string? HeadBranch,
    [property: JsonPropertyName("head_repository")] RepositoryApiResponse? HeadRepository
);

/// <summary>The one field read from the repo a run's branch lives in: its <c>owner/name</c>, which
/// says whether that branch is the watched repo's own or a fork's.</summary>
internal sealed record RepositoryApiResponse
(
    [property: JsonPropertyName("full_name")] string? FullName
);

/// <summary>The JSON body of a GitHub "list jobs for a workflow run" REST response.</summary>
internal sealed record WorkflowJobsApiResponse
(
    [property: JsonPropertyName("jobs")] IReadOnlyList<WorkflowJobApiResponse>? Jobs
);

internal sealed record WorkflowJobApiResponse
(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("conclusion")] string? Conclusion
);

/// <summary>Its own <see cref="JsonSerializerContext"/> rather than a shared one, like every other
/// host here: splitting one context's <c>[JsonSerializable]</c> attributes across multiple files
/// trips a source-generator bug (duplicate-hint-name failure), so each file that declares response
/// DTOs declares the context for them too.</summary>
[JsonSerializable(typeof(WorkflowRunApiResponse))]
[JsonSerializable(typeof(WorkflowJobsApiResponse))]
internal partial class GitHubActionsApiJsonContext : JsonSerializerContext { }
