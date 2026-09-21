using Rix.Process;
using System.Text;

namespace Rix.Repository;

/// <summary>The <c>git</c> half of talking to GitHub: runs the binary and owns the credential
/// injection, and nothing else. Split from the repo hosts so that "how a git command is run" is
/// stated once and both <see cref="IJobRepoHost"/> and <see cref="ISubmitRepoHost"/> get the exact
/// same treatment of the token — rather than one of them reaching into the other's private runner
/// to borrow it.</summary>
internal sealed class GitCli
{
    private readonly RunProcessAsync _runProcess;
    private readonly IReadOnlyDictionary<string, string> _authEnv;

    internal GitCli(GitReadToken token, RunProcessAsync runProcess)
    {
        _runProcess = runProcess;
        _authEnv = BuildAuthEnv(token);
    }

    /// <summary>
    /// Builds environment overrides for git HTTPS auth without ever placing the token in argv (visible via <c>ps</c>)
    /// or persisting it into the clone's <c>.git/config</c> remote URL. Git reads these <c>GIT_CONFIG_*</c> variables
    /// as ad-hoc config, so the credential is supplied only via the git subprocess environment for each invocation.
    /// </summary>
    private static Dictionary<string, string> BuildAuthEnv(GitReadToken token)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token.Value}"));
        return new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}",
        };
    }

    /// <summary>Runs <c>git</c> and fails the way every caller here wants it to: any non-zero exit
    /// becomes a <see cref="RepoHostException"/> naming the subcommand. The credential is
    /// injected only when <paramref name="authenticated"/> is set, so local-only commands (e.g.
    /// <c>bundle create</c>) never hand the token to a subprocess with no use for it.</summary>
    internal async Task RunAsync
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
    internal async Task<ProcessResult> TryRunAsync
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
        return await _runProcess("git", args, workingDirectory, env, null, cancellationToken);
    }
}
