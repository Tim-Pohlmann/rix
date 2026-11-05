using Rix.Job;
using System.CommandLine;

namespace Rix.Cli;

internal static class JobCommand
{
    internal static Command Build()
    {
        var command = new Command("job", "Clone a repo, run a coding agent against it, and write output bundles");

        JobOptions.AddTo(command);
        command.Options.Add(JobOptions.PromptOption);
        command.Options.Add(JobOptions.PromptFileOption);
        command.Options.Add(JobOptions.AllowedPushBranchesOption);
        command.Options.Add(JobOptions.FactoryRepoOption);
        command.Options.Add(JobOptions.AgentHomePathOption);

        return command;
    }

    internal static JobConfig CreateConfig(ParseResult parsed, IFileSystem fileSystem)
    {
        // Read in the order problems should be reported: the first failing read is the one the
        // user sees.
        var repo = CommonOptions.ReadRepo(parsed);
        var prompt = JobOptions.ReadPrompt(parsed, fileSystem);
        var readToken = JobOptions.ReadReadToken(parsed);
        var agent = JobOptions.ReadAgent(parsed);
        var maxTokens = JobOptions.ReadMaxTokens(parsed);
        var timeout = JobOptions.ReadTimeout(parsed);
        var workDir = CommonOptions.ReadWorkDir(parsed, fileSystem);
        var outputDir = JobOptions.ReadOutputDir(parsed, fileSystem);
        var model = JobOptions.ReadModel(parsed);
        var apiKey = JobOptions.ReadAgentApiKey(parsed);
        var credential = JobOptions.ReadAgentCredential(parsed, agent, apiKey);
        var allowedPushBranches = BranchName.ParseAllowList(parsed.Str(JobOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES"));
        var agentHome = JobOptions.ReadAgentHome(parsed, fileSystem);

        return new JobConfig
        (
            repo,
            readToken,
            timeout,
            WorkDir: workDir,
            OutputDir: outputDir,
            new AgentConfig(agent, prompt, maxTokens, model, credential),
            allowedPushBranches,
            agentHome
        );
    }

    /// <summary>Runs <paramref name="config"/> against the production context. Separate from
    /// <see cref="ExecuteAsync"/> so a test supplies a context of its own and never reaches
    /// <see cref="Composition"/>.</summary>
    internal static Task<int> ExecuteDefaultAsync(JobConfig config, CancellationToken cancellationToken)
    => ExecuteAsync(config, DefaultContext(config), cancellationToken);

    /// <summary>What a job runs against in production: git and a coding agent, both chosen from
    /// <paramref name="config"/>, plus the process runner, the log and the disk. The one git client
    /// reads both the job's repo and the factory repo holding the agent home files.</summary>
    internal static JobContext DefaultContext(JobConfig config)
    => new
    (
        Composition.GitHubGit(config.ReadToken, config.WorkDir),
        Composition.ProcessRunner(),
        Composition.SelectAgent(config.Agent.Kind),
        Composition.Log(),
        Composition.FileSystem()
    );

    /// <summary>What a CLI adds to a run: the result on stdout for a human or a shell watching the
    /// command, and an exit code. The artifacts the job itself produces are
    /// <see cref="JobRunner.RunAsync"/>'s (see <see cref="JobArtifacts"/>), so nothing here can leave a
    /// job half-recorded. The same JSON has already gone to <c>result.json</c>, which is why losing
    /// stdout to a broken pipe costs nothing but the echo.</summary>
    internal static async Task<int> ExecuteAsync(JobConfig config, JobContext context, CancellationToken cancellationToken)
    {
        var result = await JobRunner.RunAsync(config, context, cancellationToken);
        await BestEffortOutput.WriteLineAsync(Console.Out, JobArtifacts.Serialize(result));
        return ExitCode(result);
    }

    /// <summary>The three ways a job ends, as the three exit codes a caller distinguishes. Matches
    /// disjoint concrete types, so adding a fourth result breaks here rather than being reported as
    /// one of these.</summary>
    private static int ExitCode(IJobResult result)
    => result switch
    {
        JobSuccess => ExitCodes.Success,
        JobFailure => ExitCodes.JobFailed,
        SetupFailure => ExitCodes.SetupFailed,
        _ => throw new NotSupportedException($"Unexpected job result type: {result.GetType()}"),
    };
}
