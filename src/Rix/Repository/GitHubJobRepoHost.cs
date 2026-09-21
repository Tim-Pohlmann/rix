using Rix.Process;

namespace Rix.Repository;

/// <summary>The read-only GitHub host behind <c>rix job</c>, scoped to one repo: the clone, the
/// local inspection and the bundling that command needs, expressed as git commands plus one REST
/// lookup. Owns neither the
/// git invocation nor the HTTP client — both arrive as collaborators, so <see cref="GitHubSubmitRepoHost"/>
/// can layer its writes on the same two without reaching into this class for them.</summary>
internal sealed class GitHubJobRepoHost : IJobRepoHost
{
    private readonly GitCli _git;
    private readonly GitHubApi _api;

    internal GitHubJobRepoHost(GitCli git, GitHubApi api)
    {
        _git = git;
        _api = api;
    }

    internal GitHubJobRepoHost(RepoIdentifier repo, GitReadToken token, RunProcessAsync runProcess, HttpMessageHandler? handler = null)
        : this(new GitCli(token, runProcess), new GitHubApi(repo, token, handler)) { }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    => _git.RunAsync
    (
        ["clone", $"https://github.com/{_api.Repo.Value}.git", targetDirectory],
        Path.GetTempPath(),
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
    => _git.RunAsync
    (
        // --end-of-options stops git from reading a branch name starting with "-" as an option —
        // BranchName never validates its format. Unlike "--", it doesn't repurpose the range
        // argument as a pathspec, so "bundle create" still treats it as a revision range.
        ["bundle", "create", bundlePath, "--end-of-options", $"{baseBranch.Value}..{branch.Value}"],
        repoDirectory,
        authenticated: false,
        cancellationToken
    );

    /// <summary>Sets <c>user.name</c> and <c>user.email</c> inside the clone so the coding agent's
    /// commits carry the <see cref="GitIdentity"/> instead of whatever the agent would otherwise
    /// guess. Purely local, so no auth env is needed; each key is set in its own invocation so a
    /// failure names the key it failed on.</summary>
    public async Task ConfigureGitAsync(string repoDirectory, CancellationToken cancellationToken)
    {
        await _git.RunAsync(["config", "user.name", GitIdentity.Name], repoDirectory, authenticated: false, cancellationToken);
        await _git.RunAsync(["config", "user.email", GitIdentity.Email], repoDirectory, authenticated: false, cancellationToken);
    }

    public async Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    {
        // A missing ref (exit 1) is an expected outcome here, not a failure of the git binary itself,
        // so this asks for the raw result rather than the throw-on-failure call used above. Any other
        // failure (bad working directory, git missing, timeout, ...) is a real operational problem and
        // must still throw, or it would surface later as a misleading "branch not found". Purely
        // local, like bundle create, so no auth env is needed.
        var result = await _git.TryRunAsync
        (
            ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch.Value}"],
            repoDirectory,
            authenticated: false,
            cancellationToken
        );
        if (result is ProcessFailure { Reason: not "exited with code 1" } f)
            throw new RepoHostException($"git rev-parse failed: {f.Reason}");
        return result is ProcessSuccess;
    }

    public async Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    {
        // 404 is this endpoint's way of saying "no such branch", so the status is read here rather
        // than handed to GetJsonAsync, which treats every non-success status as a fault.
        using var response = await _api.GetAsync($"branches/{Uri.EscapeDataString(branch.Value)}", HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        GitHubApi.EnsureSuccess(response, $"check branch {branch.Value} on remote");
        return true;
    }
}
