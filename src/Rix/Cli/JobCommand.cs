using Rix.Job;
using System.CommandLine;
using System.Text.Json;

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

    /// <summary>Takes the prompt's text rather than reading it, since it may sit in a file: see
    /// <see cref="JobOptions.ReadPrompt"/>.</summary>
    internal static JobConfig ReadConfig(ParseResult parsed, string systemTempDirectory, string userHomeDirectory, string prompt)
    {
        // Read in the order problems should be reported: the first failing read is the one the
        // user sees.
        var repo = CommonOptions.ReadRepo(parsed);
        var readToken = JobOptions.ReadReadToken(parsed);
        var agent = JobOptions.ReadAgent(parsed);
        var maxTokens = JobOptions.ReadMaxTokens(parsed);
        var timeout = JobOptions.ReadTimeout(parsed);
        var workDir = CommonOptions.ReadWorkDir(parsed, systemTempDirectory);
        var outputDir = JobOptions.ReadOutputDir(parsed);
        var model = JobOptions.ReadModel(parsed);
        var apiKey = JobOptions.ReadAgentApiKey(parsed);
        var credential = JobOptions.ReadAgentCredential(parsed, agent, apiKey);
        var allowedPushBranches = BranchName.ParseAllowList(parsed.Str(JobOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES"));
        var agentHome = JobOptions.ReadAgentHome(parsed, userHomeDirectory);

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

    internal static IReadOnlyList<RequiredDirectory> RequiredDirectories(JobConfig config)
    => [RequiredDirectory.For(CommonOptions.WorkDirOption, config.WorkDir), RequiredDirectory.For(JobOptions.OutputDirOption, config.OutputDir), .. JobOptions.RunnerHome(config.AgentHome)];

    /// <summary>Runs <paramref name="config"/> against the production context. Separate from
    /// <see cref="ExecuteAsync"/> so a test supplies a context of its own and never reaches
    /// <see cref="Composition"/>.</summary>
    internal static Task<int> ExecuteDefaultAsync(JobConfig config, CancellationToken cancellationToken)
    => ExecuteAsync(config, Composition.DefaultContext(config), cancellationToken);

    /// <summary>
    /// Imperative shell around the pure-ish <see cref="JobRunner.RunAsync"/> core: runs the job,
    /// then performs all output effects — forwards the agent's stream to stderr, writes the result
    /// JSON to stdout, persists <c>result.json</c> regardless of outcome, writes the agent's
    /// extracted transcript to <c>transcript.md</c>, and maps the result to an exit code. Writing
    /// <c>result.json</c> on failure too (not just success) means callers — including
    /// <c>rix submit</c>, which already rejects a non-success <c>result.json</c> — have one
    /// reliable place to read the outcome from, instead of scraping stdout. <c>transcript.md</c>
    /// is best-effort the same way: it's a human-readable log of what the agent said/did, skipped
    /// entirely when nothing was extracted.
    /// </summary>
    internal static async Task<int> ExecuteAsync(JobConfig config, JobContext context, CancellationToken cancellationToken)
    {
        var transcriptLines = new List<string>();
        var result = await JobRunner.RunAsync(config, Teeing(context, transcriptLines), cancellationToken);
        return await WriteJobResultAsync(config, context.FileSystem.WriteAllTextAsync, result, transcriptLines);
    }

    /// <summary>Wraps <paramref name="context"/>'s transcript sink so every line it emits is also
    /// collected into <paramref name="transcriptLines"/>, which <see cref="WriteJobResultAsync"/>
    /// later writes to <c>transcript.md</c>. Tees rather than replaces, so whatever the context
    /// already did with each line (printing it, in the default case) still happens.</summary>
    private static JobContext Teeing(JobContext context, List<string> transcriptLines)
    {
        var transcriptSink = context.TranscriptLine;
        return context with { TranscriptLine = line => { transcriptSink(line); transcriptLines.Add(line); } };
    }

    /// <summary>
    /// Writes a job's outcome the same way regardless of what led to it: the result JSON to
    /// stdout, <c>result.json</c> to <paramref name="config"/>'s output dir (even on failure, so
    /// downstream tooling has one reliable place to read the outcome from), and
    /// <c>transcript.md</c> if the agent said anything worth keeping.
    /// </summary>
    private static async Task<int> WriteJobResultAsync
    (
        JobConfig config, WriteFileAsync write, IJobResult result, List<string> transcriptLines
    )
    {
        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        await BestEffortOutput.WriteLineAsync(Console.Out, json);
        await BestEffortOutput.WriteAsync(write, config.OutputDir, "result.json", json);
        await WriteTranscriptAsync(config, write, transcriptLines);
        return result switch
        {
            JobSuccess => ExitCodes.Success,
            JobFailure => ExitCodes.JobFailed,
            SetupFailure => ExitCodes.SetupFailed,
            _ => throw new NotSupportedException($"Unexpected job result type: {result.GetType()}"),
        };
    }

    /// <summary>
    /// Persists the collected agent transcript to <c>transcript.md</c> in the output directory,
    /// joining each extracted chunk with a blank line. Skipped entirely when nothing was extracted,
    /// so the artifact only exists when there is content.
    /// </summary>
    private static async Task WriteTranscriptAsync(JobConfig config, WriteFileAsync write, List<string> transcriptLines)
    {
        if (transcriptLines.Count == 0) return;
        await BestEffortOutput.WriteAsync(write, config.OutputDir, "transcript.md", string.Join("\n\n", transcriptLines));
    }
}
