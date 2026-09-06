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
internal sealed class GitHubReadHost : IRepositoryReadHost, ICiFailureHost
{
    private readonly RunProcessAsync _runProcess;
    private readonly IReadOnlyDictionary<string, string> _gitAuthEnv;

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
        ["bundle", "create", bundlePath, $"{baseBranch.Value}..{branch.Value}"],
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
            throw new InvalidOperationException($"git rev-parse failed: {f.Reason}");
        return result is ProcessSuccess;
    }

    public async Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{Repo.Value}/branches/{Uri.EscapeDataString(branch.Value)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Fetches a run's conclusion, title, URL and head branch — the facts needed to decide
    /// whether it failed and to describe the failure.</summary>
    public async Task<WorkflowRun> GetRunAsync(long runId, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{Repo.Value}/actions/runs/{runId}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var run = await ReadJsonAsync(response, GitHubReadApiJsonContext.Default.WorkflowRunApiResponse, cancellationToken);
        if (run.Conclusion is null || run.DisplayTitle is null || run.HtmlUrl is null || run.HeadBranch is null)
            throw new HttpRequestException($"get workflow run {runId} response was missing a required field");
        return new WorkflowRun(run.Conclusion, run.DisplayTitle, run.HtmlUrl, run.HeadBranch);
    }

    /// <summary>Concatenates the logs of every job that failed in the run, fetched concurrently since
    /// each job's log is independent. Relies on .NET's default redirect handling, which strips the
    /// <c>Authorization</c> header when a redirect crosses to a different host — this endpoint always
    /// 302s to short-lived, pre-signed blob storage URLs that reject an unexpected auth header, so the
    /// token must not follow.</summary>
    public async Task<string> GetFailedJobLogsAsync(long runId, CancellationToken cancellationToken)
    {
        var jobsUrl = $"https://api.github.com/repos/{Repo.Value}/actions/runs/{runId}/jobs";
        using var jobsResponse = await Http.GetAsync(jobsUrl, cancellationToken);
        jobsResponse.EnsureSuccessStatusCode();
        var jobs = await ReadJsonAsync(jobsResponse, GitHubReadApiJsonContext.Default.WorkflowJobsApiResponse, cancellationToken);

        var logs = await Task.WhenAll(jobs.Jobs.Where(j => j.Conclusion == "failure").Select(job => GetJobLogAsync(job.Id, cancellationToken)));
        return string.Join("\n", logs);
    }

    private async Task<string> GetJobLogAsync(long jobId, CancellationToken cancellationToken)
    {
        var logUrl = $"https://api.github.com/repos/{Repo.Value}/actions/jobs/{jobId}/logs";
        using var logResponse = await Http.GetAsync(logUrl, cancellationToken);
        logResponse.EnsureSuccessStatusCode();
        return await logResponse.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Finds the number of the open PR whose head is <paramref name="branch"/>, or
    /// <c>null</c> if there isn't one. Scoped to <see cref="RepoIdentifier.Owner"/>, so this only
    /// finds same-repo branches, never a fork's.</summary>
    public async Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var head = Uri.EscapeDataString($"{Repo.Owner}:{branch.Value}");
        var url = $"https://api.github.com/repos/{Repo.Value}/pulls?state=open&head={head}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var pulls = await ReadJsonAsync(response, GitHubReadApiJsonContext.Default.ListPullRequestApiResponse, cancellationToken);
        return pulls.FirstOrDefault()?.Number;
    }

    /// <summary>Shared by <see cref="GitHubHost.CreatePullRequestAsync"/> for its write-side response
    /// too, so both read and write paths wrap a malformed/empty JSON body the same way.</summary>
    internal static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        try
        {
            var value = await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken);
            if (value is null)
                throw new HttpRequestException($"{typeof(T).Name} response body was empty");
            return value;
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException($"could not parse {typeof(T).Name} response", ex);
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
            throw new InvalidOperationException($"git {args[0]} failed: {f.Reason}");
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
    [property: JsonPropertyName("jobs")] IReadOnlyList<WorkflowJobApiResponse> Jobs
);

internal sealed record WorkflowJobApiResponse
(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("conclusion")] string? Conclusion
);

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
