using Rix.Process;
using System.Net.Http.Headers;

namespace Rix.Repository;

/// <summary>Read-only GitHub host for one repo. Owns the shared transport — an authenticated
/// <see cref="HttpClient"/> for the REST API and a <see cref="GitCli"/> for HTTPS git commands —
/// which <see cref="GitHubHost"/> composes and reuses for its write operations.</summary>
internal sealed class GitHubReadHost : IRepositoryReadHost
{
    private readonly GitCli _git;

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
        _git = new GitCli(token, runProcess);
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
        var result = await _git.RunRawAsync
        (
            ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch.Value}"],
            repoDirectory, authenticated: false, cancellationToken
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

    /// <summary>Runs <c>git</c> via the shared <see cref="GitCli"/>, injecting the credential only
    /// when <paramref name="authenticated"/> is set. Kept as an instance method rather than inlined
    /// to <c>_git</c> so the composing <see cref="GitHubHost"/> can reach it for its push.</summary>
    internal Task RunGitAsync
    (
        string[] args, string workingDirectory, bool authenticated, CancellationToken cancellationToken
    )
    => _git.RunAsync(args, workingDirectory, authenticated, cancellationToken);
}
