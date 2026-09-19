using Rix.Process;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Rix.Repository;

/// <summary>Read-only GitHub host for one repo. Owns the shared transport — an authenticated
/// <see cref="HttpClient"/> for the REST API and the git auth environment for HTTPS git commands —
/// which <see cref="GitHubHost"/> composes and reuses for its write operations.</summary>
internal sealed class GitHubReadHost : IRepositoryReadHost, IGitHubCiFailureHost
{
    private readonly RunProcessAsync _runProcess;
    private readonly IReadOnlyDictionary<string, string> _gitAuthEnv;

    /// <summary>GitHub's maximum page size for the jobs endpoint, so a run's jobs are walked in as
    /// few round trips as the API allows. Also the signal that ends the walk: a page holding fewer
    /// than this many jobs is the last one.</summary>
    private const int JobsPageSize = 100;

    /// <summary>How much of a job log is held at once while streaming it. Only ever this much on top
    /// of the tail being kept, no matter how large the log is.</summary>
    private const int LogChunkChars = 8192;

    /// <summary>How many failed jobs the excerpt covers at most. The caller's budget is split evenly
    /// across the jobs included, so every extra job shrinks all the others' share; past a handful
    /// each slice is too short to show a stack trace, which is worse than covering fewer jobs well.
    /// A matrix that fails in twenty configurations is almost always failing for one reason, so the
    /// jobs left out are named in the excerpt rather than covered.</summary>
    private const int MaxJobsInExcerpt = 5;

    /// <summary>The target repo, exposed so the composing <see cref="GitHubHost"/> can build REST
    /// URLs without keeping a second copy.</summary>
    internal RepoIdentifier Repo { get; }

    /// <summary>The authenticated REST client, shared with the composing <see cref="GitHubHost"/> so
    /// its write path reuses the same connection pool and auth headers.</summary>
    internal HttpClient Http { get; }

    internal GitHubReadHost(RepoIdentifier repo, GitReadToken token, RunProcessAsync runProcess, HttpMessageHandler? handler = null)
    {
        Repo = repo;
        Http = BuildHttpClient(token, handler);
        _runProcess = runProcess;
        _gitAuthEnv = BuildGitAuthEnv(token);
    }

    private static HttpClient BuildHttpClient(GitReadToken token, HttpMessageHandler? handler)
    {
        var client = handler switch
        {
            null => new HttpClient(),
            var h => new HttpClient(h),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("rix/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    /// <summary>
    /// Builds environment overrides for git HTTPS auth without ever placing the token in argv (visible via <c>ps</c>)
    /// or persisting it into the clone's <c>.git/config</c> remote URL. Git reads these <c>GIT_CONFIG_*</c> variables
    /// as ad-hoc config, so the credential is supplied only via the git subprocess environment for each invocation.
    /// </summary>
    private static Dictionary<string, string> BuildGitAuthEnv(GitReadToken token)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token.Value}"));
        return new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}",
        };
    }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    => RunGitAsync
    (
        ["clone", $"https://github.com/{Repo.Value}.git", targetDirectory],
        workingDirectory: Path.GetTempPath(),
        authenticated: true,
        cancellationToken
    );

    public Task CreateBundleAsync
    (
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken
    )
    => RunGitAsync
    (
        // --end-of-options stops git from reading a branch name starting with "-" as an option —
        // BranchName never validates its format. Unlike "--", it doesn't repurpose the range
        // argument as a pathspec, so "bundle create" still treats it as a revision range.
        ["bundle", "create", bundlePath, "--end-of-options", $"{baseBranch.Value}..{branch.Value}"],
        workingDirectory: repoDirectory,
        authenticated: false,
        cancellationToken
    );

    /// <summary>Sets <c>user.name</c> and <c>user.email</c> inside the clone so the coding agent's
    /// commits carry the <see cref="GitIdentity"/> instead of whatever the agent would otherwise
    /// guess. Purely local, so no auth env is needed; each key is set in its own invocation so a
    /// failure names the key it failed on.</summary>
    public async Task ConfigureGitAsync(string repoDirectory, CancellationToken cancellationToken)
    {
        await RunGitAsync(["config", "user.name", GitIdentity.Name], repoDirectory, authenticated: false, cancellationToken);
        await RunGitAsync(["config", "user.email", GitIdentity.Email], repoDirectory, authenticated: false, cancellationToken);
    }

    public async Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    {
        // A missing ref (exit 1) is an expected outcome here, not a failure of the git binary itself,
        // so this reads the ProcessResult directly rather than going through RunGitAsync (which throws
        // on any non-zero exit). Any other failure (bad working directory, git missing, timeout, ...)
        // is a real operational problem and must still throw, or it would surface later as a
        // misleading "branch not found". Purely local, like bundle create, so no auth env is needed.
        var result = await _runProcess
        (
            "git", ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch.Value}"],
            repoDirectory, environmentOverrides: null, onStdoutLine: null, cancellationToken
        );
        if (result is ProcessFailure { Reason: not "exited with code 1" } f)
            throw new RepositoryHostException($"git rev-parse failed: {f.Reason}");
        return result is ProcessSuccess;
    }

    public async Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(Url($"branches/{Uri.EscapeDataString(branch.Value)}"), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        EnsureSuccess(response, $"check branch {branch.Value} on remote");
        return true;
    }

    /// <summary>Fetches a run's conclusion, title, URL and head branch — the facts needed to decide
    /// whether it failed and to describe the failure. <c>conclusion</c> is the one field GitHub
    /// itself sends as <c>null</c> (while the run is still queued/in-progress), so it's the one
    /// field this doesn't require.</summary>
    public async Task<WorkflowRun> GetRunAsync(RunId runId, CancellationToken cancellationToken)
    {
        var operation = $"get workflow run {runId.Value}";
        var run = await GetJsonAsync($"actions/runs/{runId.Value}", GitHubReadApiJsonContext.Default.WorkflowRunApiResponse, operation, cancellationToken);
        if (run.DisplayTitle is null || run.HtmlUrl is null || run.HeadBranch is null)
            throw new RepositoryHostException($"{operation} response was missing a required field");
        return new WorkflowRun(run.Conclusion, run.DisplayTitle, run.HtmlUrl, run.HeadBranch);
    }

    /// <summary>Builds one excerpt covering every job that failed in the run, each job's tail under a
    /// heading naming it — without which a multi-job failure reads as one undifferentiated log whose
    /// parts can't be told apart. <paramref name="totalTailChars"/> is shared evenly between the jobs
    /// included rather than granted to each, so the excerpt as a whole stays inside the budget the
    /// caller has to fit into a prompt however many jobs failed. Logs are fetched concurrently since
    /// each is independent. Relies on .NET's default redirect handling, which strips the
    /// <c>Authorization</c> header when a redirect crosses to a different host — this endpoint always
    /// 302s to short-lived, pre-signed blob storage URLs that reject an unexpected auth header, so the
    /// token must not follow.</summary>
    public async Task<string> GetFailedJobLogsAsync(RunId runId, int totalTailChars, CancellationToken cancellationToken)
    {
        var failedJobs = await ListFailedJobsAsync(runId, cancellationToken);
        var included = failedJobs.Take(MaxJobsInExcerpt).ToList();
        if (included.Count == 0)
            return "";

        // At least one character each, so a budget smaller than the job count still yields a log
        // rather than a division that truncates to nothing.
        var tailCharsPerJob = Math.Max(totalTailChars / included.Count, 1);
        var logs = await Task.WhenAll(included.Select(job => GetJobLogAsync(job.Id, tailCharsPerJob, cancellationToken)));
        var excerpt = string.Join("\n", included.Select((job, index) => $"===== {job.Name} =====\n{logs[index]}"));
        return (failedJobs.Count - included.Count) switch
        {
            0 => excerpt,
            var omitted => $"{excerpt}\n===== {omitted} further failed job(s) omitted =====",
        };
    }

    /// <summary>Collects the run's failed jobs across every page of the jobs endpoint, which is
    /// paginated. A matrix build can easily exceed one page, and the job that failed is no more
    /// likely to be on the first page than the last, so stopping there would silently produce an
    /// empty or partial log excerpt for exactly the runs this command exists to explain. Pages are
    /// read sequentially rather than concurrently because how many there are isn't known until a
    /// short page ends the walk. A job GitHub reports without a name falls back to its ID, which is
    /// still enough to tell one block of the excerpt from another.</summary>
    private async Task<List<FailedJob>> ListFailedJobsAsync(RunId runId, CancellationToken cancellationToken)
    {
        var operation = $"list jobs for run {runId.Value}";
        var failedJobs = new List<FailedJob>();
        var page = 1;
        while (true)
        {
            var jobs = await GetJsonAsync
            (
                $"actions/runs/{runId.Value}/jobs?per_page={JobsPageSize}&page={page}",
                GitHubReadApiJsonContext.Default.WorkflowJobsApiResponse,
                operation,
                cancellationToken
            );
            if (jobs.Jobs is null)
                throw new RepositoryHostException($"{operation} response was missing the jobs field");

            failedJobs.AddRange
            (
                jobs.Jobs
                    .Where(job => job.Conclusion == "failure")
                    .Select(job => new FailedJob(job.Id, job.Name ?? $"job {job.Id}"))
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
        using var logResponse = await Http.GetAsync(Url($"actions/jobs/{jobId}/logs"), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess(logResponse, $"get logs for job {jobId}");

        using var reader = new StreamReader(await logResponse.Content.ReadAsStreamAsync(cancellationToken));
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
        var head = Uri.EscapeDataString($"{Repo.Owner}:{branch.Value}");
        var pulls = await GetJsonAsync($"pulls?state=open&head={head}", GitHubReadApiJsonContext.Default.ListPullRequestApiResponse, $"look up open PR for branch {branch.Value}", cancellationToken);
        return pulls.FirstOrDefault()?.Number;
    }

    /// <summary>Builds a URL for <paramref name="path"/> under this host's repo, so the API base
    /// address is written once rather than at every call site.</summary>
    private string Url(string path) => $"https://api.github.com/repos/{Repo.Value}/{path}";

    /// <summary>GETs <paramref name="path"/> and parses the JSON body, collapsing the
    /// request/status-check/parse sequence every read endpoint here would otherwise repeat. Only
    /// for endpoints where any non-success status is a genuine failure — <see
    /// cref="BranchExistsOnRemoteAsync"/> reads 404 as an answer, so it calls
    /// <see cref="Http"/> directly. <paramref name="operation"/> names the call in the
    /// <see cref="RepositoryHostException"/> a failed status produces.</summary>
    private async Task<T> GetJsonAsync<T>(string path, JsonTypeInfo<T> typeInfo, string operation, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(Url(path), cancellationToken);
        EnsureSuccess(response, operation);
        return await ReadJsonAsync(response, typeInfo, cancellationToken);
    }

    /// <summary>Turns any non-2xx response into a <see cref="RepositoryHostException"/> naming the
    /// operation, so every REST call reports an error status the same way instead of leaking
    /// <see cref="HttpRequestException"/> from a bare <c>EnsureSuccessStatusCode</c>. Shared with
    /// <see cref="GitHubHost"/> for its write-side calls.</summary>
    internal static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new RepositoryHostException($"{operation} failed: {ex.Message}", ex);
        }
    }

    /// <summary>Shared by <see cref="GitHubHost.CreatePullRequestAsync"/> for its write-side response
    /// too, so both read and write paths wrap a malformed/empty JSON body the same way.</summary>
    internal static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
            if (value is null)
                throw new RepositoryHostException($"{typeof(T).Name} response body was empty");
            return value;
        }
        catch (JsonException ex)
        {
            throw new RepositoryHostException($"could not parse {typeof(T).Name} response", ex);
        }
    }

    /// <summary>Runs <c>git</c>, injecting the credential only when <paramref name="authenticated"/>
    /// is set. Local-only commands (e.g. <c>bundle create</c>) pass <c>false</c> so the token never
    /// reaches a subprocess that has no need for it; remote commands (clone, push) pass <c>true</c>.
    /// Shared with the composing <see cref="GitHubHost"/> so its push reuses this exact injection.</summary>
    internal async Task RunGitAsync
    (
        string[] args, string workingDirectory, bool authenticated, CancellationToken cancellationToken
    )
    {
        // Only the GIT_CONFIG_* auth variables are ever overridden; the subprocess still inherits the
        // full parent environment (PATH, HOME, ...) on top of these, so we never force those here.
        var env = authenticated switch
        {
            true => (IReadOnlyDictionary<string, string>?)_gitAuthEnv,
            false => null,
        };
        var result = await _runProcess("git", args, workingDirectory, env, null, cancellationToken);
        if (result is ProcessFailure f)
            throw new RepositoryHostException($"git {args[0]} failed: {f.Reason}");
    }
}

/// <summary>The JSON body of a GitHub "get a workflow run" REST response.</summary>
internal sealed record WorkflowRunApiResponse
(
    [property: JsonPropertyName("conclusion")] string? Conclusion,
    [property: JsonPropertyName("display_title")] string? DisplayTitle,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("head_branch")] string? HeadBranch
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

/// <summary>One job of a run that failed, reduced to what the excerpt needs: the ID to fetch its log
/// by and the name to head its block with.</summary>
internal sealed record FailedJob(long Id, string Name);

/// <summary>The one field <c>rix ci-failure</c> reads from a "list pull requests" REST response.</summary>
internal sealed record PullRequestApiResponse
(
    [property: JsonPropertyName("number")] int Number
);

/// <summary>Separate from <see cref="GitHubApiJsonContext"/> (defined in <c>GitHubHost.cs</c>):
/// splitting one <see cref="JsonSerializerContext"/>'s <c>[JsonSerializable]</c> attributes across
/// multiple files trips a source-generator bug (duplicate-hint-name failure), so read-side DTOs get
/// their own context instead.</summary>
[JsonSerializable(typeof(WorkflowRunApiResponse))]
[JsonSerializable(typeof(WorkflowJobsApiResponse))]
[JsonSerializable(typeof(List<PullRequestApiResponse>))]
internal partial class GitHubReadApiJsonContext : JsonSerializerContext { }
