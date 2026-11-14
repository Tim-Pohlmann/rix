using Rix.Artifacts;
using Rix.CiFailure;
using System.CommandLine;

namespace Rix.Cli;

internal static class CiFailureCommand
{
    /// <summary>The two options this command adds on top of the few it borrows, kept private for
    /// the same reason <c>agent</c> keeps <c>--prompt</c> to itself: no other command takes
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

        // Three shared options, not AgentOptions.AddTo's whole agent-running set: this command decides
        // whether to act and stops. Whoever runs the agent takes the agent's own flags.
        command.Options.Add(CommonOptions.RepoOption);
        command.Options.Add(AgentOptions.ReadTokenOption);
        command.Options.Add(AgentOptions.OutputDirOption);
        command.Options.Add(RunIdOption);
        command.Options.Add(MaxRixCommitsOption);

        return command;
    }

    internal static CiFailureConfig CreateConfig(ParseResult parsed, IFileSystem fileSystem)
    {
        // Same order as `agent` for the shared options, so both commands report the same first
        // problem for the same mistake; this command's own two come last.
        var repo = CommonOptions.ReadRepo(parsed);
        var readToken = AgentOptions.ReadReadToken(parsed);
        var outputDir = AgentOptions.ReadOutputDir(parsed, fileSystem);
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
    internal static Task<int> ExecuteDefaultAsync(CiFailureConfig config, CancellationToken cancellationToken)
    => ExecuteAsync(config, DefaultContext(config), cancellationToken);

    /// <summary>What a check runs against in production: the two GitHub hosts sharing one
    /// transport, the disk it writes its verdict to, and the log it reports a lost verdict through.
    /// Built only when the command runs, so a test that brings its own stubs opens no connection at
    /// all.</summary>
    internal static CiFailureContext DefaultContext(CiFailureConfig config)
    {
        var (ci, repoHost) = Composition.GitHubCiHosts(config.Repo, config.ReadToken);
        return new CiFailureContext(ci, repoHost, Composition.FileSystem(), Composition.Log());
    }

    /// <summary>
    /// Imperative shell around <see cref="CiFailureRunner.RunAsync"/>: reports what the check
    /// found and stops. No agent runs here, and nothing is cloned — whoever answers the failure
    /// does so from a <see cref="CiFailureDetected.Prompt"/> the check wrote, on a machine this one
    /// never touches.
    ///
    /// What a CLI adds to a check: the verdict on stdout for a human or a shell watching the
    /// command, and an exit code. The files the check leaves behind are
    /// <see cref="CiFailureRunner.RunAsync"/>'s (see <see cref="CiFailureArtifacts"/>), so nothing
    /// here can leave a check half-recorded. The same JSON has already gone to <c>result.json</c>,
    /// which is why losing stdout to a broken pipe costs nothing but the echo.
    /// </summary>
    internal static async Task<int> ExecuteAsync(CiFailureConfig config, CiFailureContext context, CancellationToken cancellationToken)
    {
        var result = await CiFailureRunner.RunAsync(config, context, cancellationToken);
        await BestEffortOutput.WriteLineAsync(Console.Out, CiFailureArtifacts.Serialize(result));
        return ExitCode(result);
    }

    /// <summary>The verdicts a caller distinguishes, as exit codes. A detected failure exits
    /// successfully like the rest: it is a verdict, not an outcome, and the caller decides what to
    /// do with it. Only <see cref="CiFailureError"/> — a problem talking to the API, or a prompt
    /// that could not be written, rather than the run itself having failed — is a failure of this
    /// command.</summary>
    private static int ExitCode(ICiFailureResult result)
    => result switch
    {
        CiFailureDetected or CiFailureSkipped or CiFailureLoopGuarded or CiFailureUntrustedRun => ExitCodes.Success,
        CiFailureError => ExitCodes.Failed,
        _ => throw new NotSupportedException($"Unexpected ci-failure result type: {result.GetType()}"),
    };
}
