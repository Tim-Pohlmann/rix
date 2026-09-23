using System.Text;
using System.Text.Json.Serialization;

namespace Rix.Repository;

/// <summary>Reads everything <c>rix ci-failure</c> needs to know about a workflow run: whether it
/// failed, what its failing jobs logged, whether a PR is open for its branch, and how much of that
/// branch's tip rix wrote itself. A separate class from <see cref="GitHubJobRepoHost"/> rather than a
/// second interface on it, because the two roles share only their transport — which they now share
/// explicitly, by being handed the same <see cref="GitHubApi"/>.</summary>
internal sealed class GitHubCiFailureRepoHost : ICiFailureRepoHost
{
    private readonly GitHubApi _api;

    /// <summary>GitHub's maximum page size for the jobs endpoint, so a run's jobs are walked in as
    /// few round trips as the API allows. Also the signal that ends the walk: a page holding fewer
    /// than this many jobs is the last one.</summary>
    private const int JobsPageSize = 100;

    /// <summary>How much of a job log is held at once while streaming it. Only ever this much on top
    /// of the tail being kept, no matter how large the log is.</summary>
    private const int LogChunkChars = 8192;

    internal GitHubCiFailureRepoHost(GitHubApi api) => _api = api;

    internal GitHubCiFailureRepoHost(RepoIdentifier repo, GitReadToken token, HttpMessageHandler? handler = null)
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
        var run = await _api.GetJsonAsync($"actions/runs/{runId.Value}", GitHubCiFailureApiJsonContext.Default.WorkflowRunApiResponse, operation, cancellationToken);
        if (run.DisplayTitle is null || run.HtmlUrl is null || run.HeadBranch is null || run.HeadRepository?.FullName is null)
            throw new RepoHostException($"{operation} response was missing a required field");
        return new WorkflowRun(run.Conclusion, run.DisplayTitle, run.HtmlUrl, run.HeadBranch, run.HeadRepository.FullName);
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
        var failedJobs = await ListFailedJobsAsync(runId, cancellationToken);
        var included = failedJobs.Take(JobLogExcerpt.MaxJobs).ToList();
        if (included.Count == 0)
            return "";

        var tailCharsPerJob = JobLogExcerpt.TailCharsPerJob(totalTailChars, included.Count);
        var logs = await Task.WhenAll(included.Select(job => GetJobLogAsync(job.Id, tailCharsPerJob, cancellationToken)));
        return JobLogExcerpt.Render(included, logs, failedJobs.Count - included.Count);
    }

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
                GitHubCiFailureApiJsonContext.Default.WorkflowJobsApiResponse,
                operation,
                cancellationToken
            );
            if (jobs.Jobs is null)
                throw new RepoHostException($"{operation} response was missing the jobs field");

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

    /// <summary>Finds the number of the open PR whose head is <paramref name="branch"/>, or
    /// <c>null</c> if there isn't one. Scoped to <see cref="RepoIdentifier.Owner"/>, so this only
    /// finds same-repo branches, never a fork's.</summary>
    public async Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var head = Uri.EscapeDataString($"{_api.Repo.Owner}:{branch.Value}");
        var pulls = await _api.GetJsonAsync($"pulls?state=open&head={head}", GitHubCiFailureApiJsonContext.Default.ListPullRequestApiResponse, $"look up open PR for branch {branch.Value}", cancellationToken);
        return pulls.FirstOrDefault()?.Number;
    }

    /// <summary>Counts the run of rix's own commits at <paramref name="branch"/>'s tip, which is how
    /// <c>rix ci-failure</c> tells "CI failed" from "CI failed on rix's last attempt to fix it".
    /// Authorship is read from <c>commit.author</c>, git's own metadata written by
    /// <see cref="GitHubJobRepoHost.ConfigureGitAsync"/>, rather than the sibling top-level
    /// <c>author</c> — that one is the linked GitHub account, which is <c>null</c> for rix precisely
    /// because <see cref="GitIdentity.Email"/> belongs to no account. One page of at most
    /// <paramref name="max"/> commits answers it: a streak that long already trips the cap, so a
    /// second page could not change the outcome.</summary>
    public async Task<int> CountLeadingRixCommitsAsync(BranchName branch, MaxRixCommits max, CancellationToken cancellationToken)
    {
        var commits = await _api.GetJsonAsync
        (
            $"commits?sha={Uri.EscapeDataString(branch.Value)}&per_page={max.Value}",
            GitHubCiFailureApiJsonContext.Default.ListCommitApiResponse,
            $"list commits on branch {branch.Value}",
            cancellationToken
        );
        return commits.TakeWhile(commit => commit.Commit?.Author?.Email == GitIdentity.Email).Count();
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

/// <summary>The JSON body of one entry of a GitHub "list commits" REST response, kept down to the
/// nesting the loop guard actually reads: <c>commit.author.email</c>.</summary>
internal sealed record CommitApiResponse
(
    [property: JsonPropertyName("commit")] CommitDetailApiResponse? Commit
);

internal sealed record CommitDetailApiResponse
(
    [property: JsonPropertyName("author")] CommitAuthorApiResponse? Author
);

internal sealed record CommitAuthorApiResponse
(
    [property: JsonPropertyName("email")] string? Email
);

/// <summary>The one field <c>rix ci-failure</c> reads from a "list pull requests" REST response.</summary>
internal sealed record PullRequestApiResponse
(
    [property: JsonPropertyName("number")] int Number
);

/// <summary>Separate from <see cref="GitHubApiJsonContext"/> (defined in <c>GitHubSubmitRepoHost.cs</c>):
/// splitting one <see cref="JsonSerializerContext"/>'s <c>[JsonSerializable]</c> attributes across
/// multiple files trips a source-generator bug (duplicate-hint-name failure), so these DTOs get
/// their own context instead.</summary>
[JsonSerializable(typeof(WorkflowRunApiResponse))]
[JsonSerializable(typeof(WorkflowJobsApiResponse))]
[JsonSerializable(typeof(List<PullRequestApiResponse>))]
[JsonSerializable(typeof(List<CommitApiResponse>))]
internal partial class GitHubCiFailureApiJsonContext : JsonSerializerContext { }
