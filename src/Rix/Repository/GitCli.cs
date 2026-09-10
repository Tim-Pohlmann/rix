using Rix.Process;

namespace Rix.Repository;

/// <summary>The one place the codebase shells out to <c>git</c>. Runs commands through the process
/// seam, injecting the github.com credential env (<see cref="GitHubAuth"/>) only for
/// <paramref name="authenticated"/> (remote) commands so the token never reaches a subprocess that
/// has no need for it. Shared by <see cref="GitHubReadHost"/> (clone/bundle/push) and
/// <see cref="GitHubFactoryContextLoader"/> (the sparse factory checkout).</summary>
internal sealed class GitCli
{
    private readonly RunProcessAsync _runProcess;
    private readonly IReadOnlyDictionary<string, string> _authEnv;

    internal GitCli(GitReadToken token, RunProcessAsync runProcess)
    {
        _runProcess = runProcess;
        _authEnv = GitHubAuth.ExtraHeaderEnv(token);
    }

    /// <summary>Runs <c>git</c> and hands back the raw <see cref="ProcessResult"/>, leaving the caller
    /// to decide which non-zero exits are expected (e.g. a missing ref from <c>rev-parse</c>).</summary>
    internal Task<ProcessResult> RunRawAsync
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

    /// <summary>Runs <c>git</c> and turns any non-zero exit into an
    /// <see cref="InvalidOperationException"/> naming the failed subcommand.</summary>
    internal async Task RunAsync
    (
        string[] args, string workingDirectory, bool authenticated, CancellationToken cancellationToken
    )
    {
        var result = await RunRawAsync(args, workingDirectory, authenticated, cancellationToken);
        if (result is ProcessFailure f)
            throw new InvalidOperationException($"git {args[0]} failed: {f.Reason}");
    }
}
