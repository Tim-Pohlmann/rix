using Rix.Artifacts;
using Rix.Agent;
using System.CommandLine;

namespace Rix.Cli;

internal static class AgentCommand
{
    internal static Command Build()
    {
        var command = new Command("agent", "Clone a repo, run a coding agent against it, and write output bundles");

        AgentOptions.AddTo(command);
        command.Options.Add(AgentOptions.PromptOption);
        command.Options.Add(AgentOptions.PromptFileOption);
        command.Options.Add(AgentOptions.AllowedPushBranchesOption);
        command.Options.Add(AgentOptions.FactoryRepoOption);
        command.Options.Add(AgentOptions.AgentHomePathOption);

        return command;
    }

    internal static AgentConfig CreateConfig(ParseResult parsed, IFileSystem fileSystem)
    {
        // Read in the order problems should be reported: the first failing read is the one the
        // user sees.
        var repo = CommonOptions.ReadRepo(parsed);
        var prompt = AgentOptions.ReadPrompt(parsed, fileSystem);
        var readToken = AgentOptions.ReadReadToken(parsed);
        var agent = AgentOptions.ReadAgent(parsed);
        var maxTokens = AgentOptions.ReadMaxTokens(parsed);
        var timeout = AgentOptions.ReadTimeout(parsed);
        var workDir = CommonOptions.ReadWorkDir(parsed, fileSystem);
        var outputDir = AgentOptions.ReadOutputDir(parsed, fileSystem);
        var model = AgentOptions.ReadModel(parsed);
        var effort = AgentOptions.ReadEffort(parsed);
        var apiKey = AgentOptions.ReadAgentApiKey(parsed);
        var credential = AgentOptions.ReadAgentCredential(parsed, agent, apiKey);
        var allowedPushBranches = BranchName.ParseAllowList(parsed.Str(AgentOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES"));
        var agentHome = AgentOptions.ReadAgentHome(parsed, fileSystem);

        return new AgentConfig
        (
            repo,
            readToken,
            timeout,
            WorkDir: workDir,
            OutputDir: outputDir,
            new CodingAgentConfig(agent, prompt, maxTokens, model, effort, credential),
            allowedPushBranches,
            agentHome
        );
    }

    /// <summary>Runs <paramref name="config"/> against the production context. Separate from
    /// <see cref="ExecuteAsync"/> so a test supplies a context of its own and never reaches
    /// <see cref="Composition"/>.</summary>
    internal static Task<int> ExecuteDefaultAsync(AgentConfig config, CancellationToken cancellationToken)
    => ExecuteAsync(config, DefaultContext(config), cancellationToken);

    /// <summary>What the agent command runs against in production: git and a coding agent, both chosen from
    /// <paramref name="config"/>, plus the process runner, the log and the disk. The one git client
    /// reads both the run's repo and the factory repo holding the agent home files.</summary>
    internal static AgentContext DefaultContext(AgentConfig config)
    => new
    (
        Composition.GitHubGit(config.ReadToken, config.WorkDir),
        Composition.ProcessRunner(),
        Composition.SelectAgent(config.Agent.Kind),
        Composition.Log(),
        Composition.FileSystem()
    );

    /// <summary>What a CLI adds to a run: the result on stdout for a human or a shell watching the
    /// command, and an exit code. The artifacts the run itself produces are
    /// <see cref="AgentRunner.RunAsync"/>'s (see <see cref="AgentArtifacts"/>), so nothing here can leave a
    /// run half-recorded. Stdout is still written when <c>result.json</c> was not, and is then the
    /// only copy of the outcome left - which is also why losing stdout to a broken pipe normally
    /// costs nothing but the echo.</summary>
    internal static async Task<int> ExecuteAsync(AgentConfig config, AgentContext context, CancellationToken cancellationToken)
    {
        var outcome = await AgentRunner.RunAsync(config, context, cancellationToken);
        await BestEffortOutput.WriteLineAsync(Console.Out, AgentArtifacts.Serialize(outcome.Result));
        return ExitCode(outcome);
    }

    /// <summary>A lost <c>result.json</c> first, whatever the run's own outcome was: the file is
    /// what <c>rix submit</c> and the job summary read, so a caller that got
    /// <see cref="ExitCodes.Success"/> or <see cref="ExitCodes.Failed"/> would go on to read a
    /// file that isn't there, and a green run is the worst of the two mistakes. The outcome itself
    /// is on stdout either way, so nothing is hidden by reporting the lost file
    /// instead.</summary>
    private static int ExitCode(AgentOutcome outcome)
    => outcome.ResultFile switch
    {
        ArtifactLost => ExitCodes.ResultNotWritten,
        ArtifactWritten => ExitCode(outcome.Result),
        _ => throw new NotSupportedException($"Unexpected artifact write type: {outcome.ResultFile.GetType()}"),
    };

    /// <summary>The three ways a run ends, as the three exit codes a caller distinguishes. Matches
    /// disjoint concrete types, so adding a fourth result breaks here rather than being reported as
    /// one of these.</summary>
    private static int ExitCode(IAgentResult result)
    => result switch
    {
        AgentSuccess => ExitCodes.Success,
        AgentFailure => ExitCodes.Failed,
        SetupFailure => ExitCodes.SetupFailed,
        _ => throw new NotSupportedException($"Unexpected agent result type: {result.GetType()}"),
    };
}
