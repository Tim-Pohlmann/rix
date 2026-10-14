using Rix.Agents;
using Rix.CiFailure;
using Rix.Initialize;
using Rix.Job;
using Rix.Process;
using Rix.Repository;
using Rix.Submit;

namespace Rix;

/// <summary>Every production service rix runs against, chosen in one place: which git client, which
/// repo and CI hosts, which coding agent, where output goes. Each command asks for the context it
/// needs and gets the real thing; a test calls the command's run with a context of its own and this
/// file never executes. Collected rather than kept beside each command because what is asserted
/// here is one decision, not four — rix talks to GitHub — and a second repo host should be a change
/// to this file alone.
///
/// Separate from <see cref="Startup"/> so the composition root is not also the process entry point:
/// <see cref="Startup"/> owns the signals and the invocation, this owns the wiring, and the
/// commands own themselves.</summary>
internal static class Composition
{
    /// <summary>The production <see cref="JobContext"/>: git against the GitHub repo, process runner,
    /// the coding agent selected by <see cref="JobConfig.Agent"/>, and stderr log sink, all wired
    /// from <paramref name="config"/>. <see cref="JobContext.TranscriptLine"/> is a no-op here;
    /// <see cref="Cli.JobCommand.ExecuteAsync"/> tees in its own collecting sink regardless of which
    /// context it ends up using.</summary>
    internal static JobContext DefaultContext(JobConfig config)
    => new
    (
        GitHubGit(config.Repo, config.ReadToken),
        ProcessWrapper.RunAsync,
        SelectAgent(config.Agent.Kind),
        // Named because LogLine and TranscriptLine are the same delegate type: transposing them
        // compiles, and would silently print the agent's transcript to stderr and drop rix's own log.
        LogLine: Console.Error.WriteLine,
        TranscriptLine: _ => { },
        AgentHomeFetcher: new AgentHomeFetcher(factoryRepo => GitHubGit(factoryRepo, config.ReadToken))
    );

    /// <summary>Git against <paramref name="repo"/> on GitHub, authenticated with
    /// <paramref name="token"/> — the one place the GitHub clone URL is spelled out.</summary>
    private static GitCli GitHubGit(RepoIdentifier repo, GitReadToken token)
    => new(new Uri($"https://github.com/{repo.Value}.git"), token, ProcessWrapper.RunAsync);

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
        return new CiFailureContext(new GitHubActionsCiHost(api), new GitHubCiFailureRepoHost(api));
    }

    /// <summary>The production <see cref="SubmitContext"/>: git and the GitHub repo host, both
    /// authenticated with the write token, the default process runner, and a stderr log sink.</summary>
    internal static SubmitContext DefaultSubmitContext(SubmitConfig config)
    => new
    (
        GitHubGit(config.Repo, config.WriteToken),
        new GitHubSubmitRepoHost(config.Repo, config.WriteToken),
        ProcessWrapper.RunAsync,
        Console.Error.WriteLine
    );

    /// <summary>The production <see cref="InitializeContext"/>: writes each template to disk via
    /// <see cref="FileWriter"/> (creating any missing parent directory), and logs to stderr.</summary>
    internal static InitializeContext DefaultInitializeContext()
    => new
    (
        FileWriter.WriteAsync,
        Console.Error.WriteLine
    );
}
