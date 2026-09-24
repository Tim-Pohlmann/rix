using Rix.Process;
using System.Text;

namespace Rix.Repository;

/// <summary><see cref="IGit"/> on the <c>git</c> binary, for the one remote given at creation. Owns
/// the credential injection: the token goes only to the commands that talk to the remote. Knows
/// nothing about any particular host - the remote's URL is an input, so a GitHub repo is only
/// GitHub-specific where that URL is built.</summary>
internal sealed class GitCli : IGit
{
    private readonly Uri _remote;
    private readonly RunProcessAsync _runProcess;
    private readonly IReadOnlyDictionary<string, string> _authEnv;

    internal GitCli(Uri remote, GitReadToken token, RunProcessAsync runProcess)
    {
        _remote = remote;
        _runProcess = runProcess;
        _authEnv = BuildAuthEnv(remote, token);
    }

    /// <summary>
    /// Builds environment overrides for git HTTPS auth without ever placing the token in argv (visible via <c>ps</c>)
    /// or persisting it into the clone's <c>.git/config</c> remote URL. Git reads these <c>GIT_CONFIG_*</c> variables
    /// as ad-hoc config, so the credential is supplied only via the git subprocess environment for each invocation,
    /// and only for requests to <paramref name="remote"/>'s host.
    /// </summary>
    private static Dictionary<string, string> BuildAuthEnv(Uri remote, GitReadToken token)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token.Value}"));
        return new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = $"http.{remote.GetLeftPart(UriPartial.Authority)}/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}",
        };
    }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    => RunAsync(["clone", _remote.AbsoluteUri, targetDirectory], Path.GetTempPath(), authenticated: true, cancellationToken);

    /// <summary>Asks the remote with <c>git ls-remote</c>. Its patterns are globs, so a branch name
    /// containing <c>*</c> would match other branches: the listed refs are compared exactly instead
    /// of trusting the exit code alone. Exit 2 is <c>--exit-code</c>'s "nothing matched" - an answer,
    /// not a fault.</summary>
    public async Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var refName = $"refs/heads/{branch.Value}";
        var listed = new List<string>();
        var result = await _runProcess
        (
            "git",
            ["ls-remote", "--exit-code", _remote.AbsoluteUri, refName],
            Path.GetTempPath(),
            _authEnv,
            listed.Add,
            cancellationToken
        );
        if (result is ProcessFailure { Reason: not "exited with code 2" } f)
            throw new RepoHostException($"git ls-remote failed: {f.Reason}");
        return listed.Exists(line => line.EndsWith($"\t{refName}", StringComparison.Ordinal));
    }

    public async Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    {
        // A missing ref (exit 1) is an expected outcome here, not a failure of the git binary itself,
        // so this asks for the raw result rather than the throw-on-failure call used above. Any other
        // failure (bad working directory, git missing, timeout, ...) is a real operational problem and
        // must still throw, or it would surface later as a misleading "branch not found". Purely
        // local, so no auth env is needed.
        var result = await TryRunAsync
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

    /// <summary>Purely local, so no auth env is needed; each key is set in its own invocation so a
    /// failure names the key it failed on.</summary>
    public async Task ConfigureIdentityAsync(string repoDirectory, CancellationToken cancellationToken)
    {
        await RunAsync(["config", "user.name", GitIdentity.Name], repoDirectory, authenticated: false, cancellationToken);
        await RunAsync(["config", "user.email", GitIdentity.Email], repoDirectory, authenticated: false, cancellationToken);
    }

    public Task CreateBundleAsync
    (
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken
    )
    => RunAsync
    (
        // --end-of-options stops git from reading a branch name starting with "-" as an option —
        // BranchName never validates its format. Unlike "--", it doesn't repurpose the range
        // argument as a pathspec, so "bundle create" still treats it as a revision range.
        ["bundle", "create", bundlePath, "--end-of-options", $"{baseBranch.Value}..{branch.Value}"],
        repoDirectory,
        authenticated: false,
        cancellationToken
    );

    public Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    => RunAsync
    (
        // --end-of-options: see CreateBundleAsync.
        ["push", "origin", "--end-of-options", branch.Value],
        repoDirectory,
        authenticated: true,
        cancellationToken
    );

    /// <summary>Runs <c>git</c> and fails the way every caller here wants it to: any non-zero exit
    /// becomes a <see cref="RepoHostException"/> naming the subcommand. The credential is
    /// injected only when <paramref name="authenticated"/> is set, so local-only commands (e.g.
    /// <c>bundle create</c>) never hand the token to a subprocess with no use for it.</summary>
    private async Task RunAsync
    (
        string[] args, string workingDirectory, bool authenticated, CancellationToken cancellationToken
    )
    {
        var result = await TryRunAsync(args, workingDirectory, authenticated, cancellationToken);
        if (result is ProcessFailure f)
            throw new RepoHostException($"git {args[0]} failed: {f.Reason}");
    }

    /// <summary>Runs <c>git</c> and hands back the raw <see cref="ProcessResult"/>, for the commands
    /// whose non-zero exit is an answer rather than a fault — <c>rev-parse --verify</c> exiting 1
    /// means "no such ref", not "git broke". Deciding which exit codes mean what is the caller's
    /// business; this only runs the command.</summary>
    private Task<ProcessResult> TryRunAsync
    (
        string[] args, string workingDirectory, bool authenticated, CancellationToken cancellationToken
    )
    {
        // Only the GIT_CONFIG_* auth variables are ever overridden; the subprocess still inherits the
        // full parent environment (PATH, HOME, ...) on top of these, so we never force those here.
        var env = authenticated switch
        {
            true => (IReadOnlyDictionary<string, string>?)_authEnv,
            false => null,
        };
        return _runProcess("git", args, workingDirectory, env, null, cancellationToken);
    }
}
