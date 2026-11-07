using Rix.Agents;
using Rix.CiFailure;
using Rix.Process;
using Rix.Repository;
using Rix.Submit;

namespace Rix;

/// <summary>Every production implementation rix runs against, chosen in one place: which git
/// client, which repo and CI hosts, which coding agent, which file system, where the log goes, how
/// a subprocess is started. That is one decision, not six — rix talks to GitHub, against the local
/// disk, with real child processes — and a second repo host should be a change to this file alone.
///
/// Each factory is typed as the implementation it returns, wherever there is only one of them:
/// naming it is this file's whole job, and it is the contexts that hold the seams.
///
/// What is *not* here is which of these each command needs: a command assembles its own context
/// from these, because that is a statement about the command rather than about the host. So this
/// file knows how to build each service and nothing about the shape of a run, and a new command
/// adds no line to it.
///
/// Separate from <see cref="Startup"/> so the composition root is not also the process entry point:
/// <see cref="Startup"/> owns the signals and the invocation, this owns the implementations, and
/// the commands own what they do with them.</summary>
internal static class Composition
{
    /// <summary>The disk. <see cref="Startup"/> asks for it here rather than choosing one of its
    /// own, so every production implementation is still named in this file — and it needs the
    /// concrete type for <see cref="LocalFileSystem.UserHomeDirectory"/>, which answers where a
    /// flag defaults to rather than what is on disk and so isn't on the seam.</summary>
    internal static LocalFileSystem FileSystem()
    => new();

    /// <summary>Real child processes.</summary>
    internal static ProcessWrapper ProcessRunner()
    => new();

    /// <summary>Diagnostics to stderr, which is where they belong when stdout carries the result a
    /// caller parses.</summary>
    internal static StderrLog Log()
    => new();

    /// <summary>Git against repos on GitHub, authenticated with <paramref name="token"/> — the one
    /// place the GitHub host is spelled out. Commands that need no local repo run from
    /// <paramref name="workDir"/>.</summary>
    internal static GitCli GitHubGit(GitReadToken token, DirectoryPath workDir)
    => new(new UriBuilder(Uri.UriSchemeHttps, "github.com").Uri, token, ProcessRunner(), workDir, Log());

    /// <summary>The coding agent <paramref name="agent"/> names. The one factory that can't be
    /// typed as its implementation: which one it is is the input.</summary>
    internal static ICodingAgent SelectAgent(AgentKind agent)
    => agent switch
    {
        AgentKind.Claude => new ClaudeAgent(),
        AgentKind.OpenCode => new OpenCodeAgent(),
        AgentKind.Pi => new PiAgent(),
        _ => throw new NotSupportedException($"Unsupported agent: {agent}"),
    };

    /// <summary>Reading a CI run and judging its branch, as two roles against the same GitHub
    /// account under the same credential: two hosts over one shared <see cref="GitHubApi"/>
    /// transport rather than two independently connected ones. Returned as a pair because sharing
    /// the transport is the part that belongs here — this is where GitHub-hosting-its-own-CI is
    /// asserted, and the seams themselves don't require it.</summary>
    internal static (GitHubActionsCiHost Ci, GitHubCiFailureRepoHost RepoHost) GitHubCiHosts
    (
        RepoIdentifier repo, GitReadToken token
    )
    {
        var api = new GitHubApi(repo, token);
        return (new GitHubActionsCiHost(api), new GitHubCiFailureRepoHost(api));
    }

    /// <summary>The GitHub side of <c>submit</c>: opening pull requests and inspecting branches,
    /// authenticated with the write token.</summary>
    internal static GitHubSubmitRepoHost GitHubSubmitHost(RepoIdentifier repo, GitToken token)
    => new(repo, token);
}
