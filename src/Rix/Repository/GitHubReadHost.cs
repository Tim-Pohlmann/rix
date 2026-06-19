using System.Net.Http.Headers;
using System.Text;
using Rix.Process;

namespace Rix.Repository;

/// <summary>Read-only GitHub host for one repo. Owns the shared transport — an authenticated
/// <see cref="HttpClient"/> for the REST API and the git auth environment for HTTPS git commands —
/// which <see cref="GitHubHost"/> composes and reuses for its write operations.</summary>
internal sealed class GitHubReadHost : IRepositoryReadHost
{
    private readonly RunProcessAsync _runProcess;
    private readonly IReadOnlyDictionary<string, string> _gitAuthEnv;

    /// <summary>The target repo, exposed so the composing <see cref="GitHubHost"/> can build REST
    /// URLs without keeping a second copy.</summary>
    internal RepoIdentifier Repo { get; }

    /// <summary>The authenticated REST client, shared with the composing <see cref="GitHubHost"/> so
    /// its write path reuses the same connection pool and auth headers.</summary>
    internal HttpClient Http { get; }

    internal GitHubReadHost(
        RepoIdentifier repo, GitReadToken token, RunProcessAsync runProcess, HttpMessageHandler? handler = null)
    {
        Repo = repo;
        Http = BuildHttpClient(token, handler);
        _runProcess = runProcess;
        _gitAuthEnv = BuildGitAuthEnv(token);
    }

    private static HttpClient BuildHttpClient(GitReadToken token, HttpMessageHandler? handler)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
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

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) =>
        RunGitAsync(["clone", $"https://github.com/{Repo.Value}.git", targetDirectory],
            workingDirectory: Path.GetTempPath(), authenticated: true, cancellationToken);

    public Task CreateBundleAsync(
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken) =>
        RunGitAsync(["bundle", "create", bundlePath, $"{baseBranch.Value}..{branch.Value}"],
            workingDirectory: repoDirectory, authenticated: false, cancellationToken);

    public async Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{Repo.Value}/branches/{Uri.EscapeDataString(branch.Value)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Runs <c>git</c>, injecting the credential only when <paramref name="authenticated"/>
    /// is set. Local-only commands (e.g. <c>bundle create</c>) pass <c>false</c> so the token never
    /// reaches a subprocess that has no need for it; remote commands (clone, push) pass <c>true</c>.
    /// Shared with the composing <see cref="GitHubHost"/> so its push reuses this exact injection.</summary>
    internal async Task RunGitAsync(
        string[] args, string workingDirectory, bool authenticated, CancellationToken cancellationToken)
    {
        // Only the GIT_CONFIG_* auth variables are ever overridden; the subprocess still inherits the
        // full parent environment (PATH, HOME, ...) on top of these, so we never force those here.
        var env = authenticated ? _gitAuthEnv : null;
        var result = await _runProcess("git", args, workingDirectory, env, null, cancellationToken);
        if (result is ProcessFailure f)
            throw new InvalidOperationException($"git {args[0]} failed: {f.Reason}");
    }
}
