using Rix.Agents;
using Rix.CiFailure;
using Rix.Initialize;
using Rix.Job;
using Rix.Process;
using Rix.Repository;
using Rix.Submit;

namespace Rix;

/// <summary>Every production service rix runs against, chosen in one place: which git client, which
/// repo and CI hosts, which coding agent, which file system. Each command asks for the context it
/// needs and gets the real thing; a test calls the command's run with a context of its own and this
/// file never executes. Collected rather than kept beside each command because what is asserted
/// here is one decision, not four — rix talks to GitHub, against the local disk — and a second repo
/// host should be a change to this file alone.
///
/// Separate from <see cref="Startup"/> so the composition root is not also the process entry point:
/// <see cref="Startup"/> owns the signals and the invocation, this owns the wiring, and the
/// commands own what they do with it.</summary>
internal static class Composition
{
    /// <summary>The disk. Separate from the contexts below because two things are read from it
    /// before any context exists: the prompt file a flag names, and the directories a config points
    /// at. <see cref="Startup"/> asks for it here rather than choosing one of its own, so every
    /// production implementation is still named in this file. Typed concretely because
    /// <see cref="Startup"/> also needs the two environment paths
    /// (<see cref="LocalFileSystem.SystemTempDirectory"/>, <see cref="LocalFileSystem.UserHomeDirectory"/>)
    /// that answer where a flag defaults to rather than what is on disk, and so aren't on the
    /// seam.</summary>
    internal static LocalFileSystem FileSystem()
    => new();

    /// <summary>The production <see cref="JobContext"/>: git against GitHub, process runner,
    /// the coding agent selected by <see cref="JobConfig.Agent"/>, and stderr log sink, all wired
    /// from <paramref name="config"/>. The one git client reads both the job's repo and the factory
    /// repo holding the agent home files. <see cref="JobContext.TranscriptLine"/> is a no-op here;
    /// <see cref="Cli.JobCommand.ExecuteAsync"/> tees in its own collecting sink regardless of which
    /// context it ends up using.</summary>
    internal static JobContext DefaultContext(JobConfig config)
    => new
    (
        GitHubGit(config.ReadToken, config.WorkDir),
        ProcessWrapper.RunAsync,
        SelectAgent(config.Agent.Kind),
        // Named because LogLine and TranscriptLine are the same delegate type: transposing them
        // compiles, and would silently print the agent's transcript to stderr and drop rix's own log.
        LogLine: Console.Error.WriteLine,
        TranscriptLine: _ => { },
        FileSystem: FileSystem()
    );

    /// <summary>Git against repos on GitHub, authenticated with <paramref name="token"/> — the one
    /// place the GitHub host is spelled out. Commands that need no local repo run from
    /// <paramref name="workDir"/>.</summary>
    private static GitCli GitHubGit(GitReadToken token, DirectoryPath workDir)
    => new(new UriBuilder(Uri.UriSchemeHttps, "github.com").Uri, token, ProcessWrapper.RunAsync, workDir.Value);

    private static ICodingAgent SelectAgent(AgentKind agent)
    => agent switch
    {
        AgentKind.Claude => new ClaudeAgent(),
        AgentKind.OpenCode => new OpenCodeAgent(),
        AgentKind.Pi => new PiAgent(),
        _ => throw new NotSupportedException($"Unsupported agent: {agent}"),
    };

    /// <summary>The production <see cref="CiFailureContext"/>: reading the run and judging its
    /// branch are two roles against the same GitHub account under the same credential, so they are
    /// two hosts over one shared <see cref="GitHubApi"/> transport rather than two independently
    /// connected ones. This is where GitHub-hosting-its-own-CI is asserted — the seams themselves
    /// don't require it, and pointing <see cref="CiFailureContext.Ci"/> at another CI provider is a
    /// change to this method alone. Built only when the command runs, so a test that brings its own
    /// stubs opens no connection at all.</summary>
    internal static CiFailureContext DefaultCiFailureContext(CiFailureConfig config)
    {
        var api = new GitHubApi(config.Repo, config.ReadToken);
        return new CiFailureContext(new GitHubActionsCiHost(api), new GitHubCiFailureRepoHost(api), FileSystem());
    }

    /// <summary>The production <see cref="SubmitContext"/>: git and the GitHub repo host, both
    /// authenticated with the write token, a stderr log sink and the local disk.</summary>
    internal static SubmitContext DefaultSubmitContext(SubmitConfig config)
    => new
    (
        GitHubGit(config.WriteToken, config.WorkDir),
        new GitHubSubmitRepoHost(config.Repo, config.WriteToken),
        Console.Error.WriteLine,
        FileSystem()
    );

    /// <summary>The production <see cref="InitializeContext"/>: writes each template to disk
    /// (creating any missing parent directory), and logs to stderr.</summary>
    internal static InitializeContext DefaultInitializeContext()
    => new
    (
        FileSystem().WriteAllTextAsync,
        Console.Error.WriteLine
    );
}
