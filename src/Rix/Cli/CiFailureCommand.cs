using Rix.CiFailure;
using System.CommandLine;
using System.Text.Json;

namespace Rix.Cli;

internal static class CiFailureCommand
{
    /// <summary>The two options this command adds on top of the few it borrows, kept private for
    /// the same reason <c>job</c> keeps <c>--prompt</c> to itself: no other command takes
    /// them.</summary>
    private static readonly Option<string> RunIdOption = new("--run-id")
    {
        Description = "ID of the (possibly failed) workflow run to inspect"
    };

    private static readonly Option<string> MaxRixCommitsOption = new("--max-rix-commits")
    {
        Description = "How many of rix's own commits may already sit at the failing branch's tip before " +
            "the failure is left alone instead of answered. Stops rix from answering its own output " +
            "indefinitely; any commit by someone else at the tip clears the count. Defaults to " +
            $"{CiFailureConfig.DefaultMaxRixCommits}, at most {MaxRixCommits.MaxValue}."
    };

    internal static Command Build()
    {
        var command = new Command
        (
            "ci-failure",
            "Check whether a workflow run failed and, if so, write the prompt a coding agent should answer it with"
        );

        // Three shared options, not JobOptions.AddTo's whole agent-running set: this command decides
        // whether to act and stops. Whoever runs the agent takes the agent's own flags.
        command.Options.Add(CommonOptions.RepoOption);
        command.Options.Add(JobOptions.ReadTokenOption);
        command.Options.Add(JobOptions.OutputDirOption);
        command.Options.Add(RunIdOption);
        command.Options.Add(MaxRixCommitsOption);

        command.SetAction(CliPipeline.ReportingInvalidInput((parsed, token) => ExecuteDefaultAsync(ReadConfig(parsed), token)));
        return command;
    }

    internal static CiFailureConfig ReadConfig(ParseResult parsed)
    {
        // Same order as `job` for the shared options, so both commands report the same first
        // problem for the same mistake; this command's own two come last.
        var repo = CommonOptions.ReadRepo(parsed);
        var readToken = JobOptions.ReadReadToken(parsed);
        var outputDir = JobOptions.ReadOutputDir(parsed);
        var runId = parsed.Required(RunIdOption, "RIX_RUN_ID", raw => new RunId(Input.WholeNumber<long>(raw)));
        var maxRixCommits = parsed.Optional
        (
            MaxRixCommitsOption,
            "RIX_MAX_RIX_COMMITS",
            raw => new MaxRixCommits(Input.WholeNumber<int>(raw)),
            new MaxRixCommits(CiFailureConfig.DefaultMaxRixCommits)
        );

        return new CiFailureConfig(runId, repo, readToken, outputDir, maxRixCommits);
    }

    /// <summary>Runs <paramref name="config"/> against the production context. Separate from
    /// <see cref="ExecuteAsync"/> so a test supplies a context of its own and never reaches
    /// <see cref="Composition"/>.</summary>
    private static Task<int> ExecuteDefaultAsync(CiFailureConfig config, CancellationToken cancellationToken)
    => ExecuteAsync(config, Composition.DefaultCiFailureContext(config), cancellationToken);

    /// <summary>
    /// Imperative shell around <see cref="CiFailureDetector.DetectAsync"/>: reports what the check
    /// found and stops. No agent runs here, and nothing is cloned — whoever answers the failure
    /// does so from a <see cref="CiFailureDetected.Prompt"/> this wrote, on a machine this one
    /// never touches.
    ///
    /// The verdict goes to stdout (for a caller reading the JSON directly) and to
    /// <c>result.json</c> (for one that would rather read a file); when a failure was detected the
    /// prompt goes to <c>prompt.md</c>, and the verdict carries its path rather than its text. The
    /// prompt is the one field built from the failing run's log output, so it is the one with no
    /// bound on its length or its content — naming the file instead of inlining it means the
    /// verdict stays short and validated, and nobody parsing it has to quote arbitrary log text
    /// back out of a field. That is also why the file is written first: the verdict only points at
    /// it once it is there, and a prompt that could not be written is reported as an error rather
    /// than announced as a detection.
    /// </summary>
    internal static async Task<int> ExecuteAsync(CiFailureConfig config, CiFailureContext context, CancellationToken cancellationToken)
    {
        var result = await CiFailureDetector.DetectAsync
        (
            config.Repo, config.RunId, context.Ci, context.RepoHost, config.MaxRixCommits, cancellationToken
        );

        if (result is CiFailureDetected detected)
            result = await WritePromptAsync(detected, config.OutputDir);

        var json = JsonSerializer.Serialize(result, CiFailureJsonContext.Default.ICiFailureResult);
        await BestEffortOutput.WriteLineAsync(Console.Out, json);
        await BestEffortOutput.WriteFileAsync(config.OutputDir, "result.json", json);

        // A detected failure exits successfully like the rest: it is a verdict, not an outcome, and
        // the caller decides what to do with it. Only CiFailureError - a problem talking to the API,
        // rather than the run itself having failed - is a failure of this command.
        return result switch
        {
            CiFailureDetected or CiFailureSkipped or CiFailureLoopGuarded or CiFailureUntrustedRun => ExitCodes.Success,
            CiFailureError => ExitCodes.JobFailed,
            _ => throw new NotSupportedException($"Unexpected ci-failure result type: {result.GetType()}"),
        };
    }

    /// <summary>Writes the prompt beside the verdict and returns the verdict naming it. Unlike
    /// <see cref="BestEffortOutput"/>'s writes, a failure is turned into a
    /// <see cref="CiFailureError"/>: this one runs before the verdict is reported rather than
    /// after, and a <c>detected</c> result whose prompt never reached disk would send a caller to a
    /// file that isn't there. Reporting it as an error instead leaves the caller with no branch and
    /// no agent started, which is what a check that couldn't finish should produce.</summary>
    private static async Task<ICiFailureResult> WritePromptAsync(CiFailureDetected detected, DirectoryPath outputDir)
    {
        var path = Path.Combine(outputDir.Value, "prompt.md");
        try
        {
            await File.WriteAllTextAsync(path, detected.Prompt, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CiFailureError($"failed to write the prompt to {path}: {ex.Message}");
        }

        return detected with { PromptFile = path };
    }
}
