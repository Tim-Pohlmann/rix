using Rix.Initialize;
using System.CommandLine;

namespace Rix.Cli;

internal static class InitializeCommand
{
    private static readonly Option<string> DirOption = new("--dir")
    {
        Description = "Target repo directory to write the workflow files into (default: current directory)"
    };

    private static readonly Option<string> RefOption = new("--ref")
    {
        Description = $"Ref of the rix repo the written workflows call, i.e. what follows '@' in their uses: lines (default: {WorkflowRef.ForThisBuild}, the major-version tag of this binary's release)"
    };

    internal static Command Build()
    {
        var command = new Command("initialize", "Write the rix caller workflows into a cloned repo's .github/workflows/");
        // `init` alias: the short form nearly everyone reaches for first.
        command.Aliases.Add("init");
        command.Options.Add(DirOption);
        command.Options.Add(RefOption);

        command.SetAction(CliPipeline.ReportingInvalidInput((parsed, token) => ExecuteDefaultAsync(ReadConfig(parsed), token)));
        return command;
    }

    internal static InitializeConfig ReadConfig(ParseResult parsed)
    {
        // Unlike the CI-run commands, `initialize` is a local dev step - no RIX_* env fallback; an
        // absent --dir just means "this repo".
        var dir = parsed.GetValue(DirOption) ?? Directory.GetCurrentDirectory();
        return new InitializeConfig
        (
            Input.Required("--dir", dir, path => new DirectoryPath(path)),
            Input.Optional("--ref", parsed.GetValue(RefOption), value => new WorkflowRef(value), WorkflowRef.ForThisBuild)
        );
    }

    /// <summary>Runs <paramref name="config"/> against the production context. Separate from
    /// <see cref="ExecuteAsync"/> so a test supplies a context of its own and never reaches
    /// <see cref="Composition"/>.</summary>
    private static Task<int> ExecuteDefaultAsync(InitializeConfig config, CancellationToken cancellationToken)
    => ExecuteAsync(config, Composition.DefaultInitializeContext(), cancellationToken);

    /// <summary>
    /// Imperative shell around <see cref="InitializeRunner.RunAsync"/>: writes the caller
    /// workflows, then prints the manual follow-up steps (secrets, the CI workflow name, commit)
    /// on success, or the error on failure, and maps the result to an exit code.
    /// </summary>
    internal static async Task<int> ExecuteAsync(InitializeConfig config, InitializeContext context, CancellationToken cancellationToken)
    {
        var result = await InitializeRunner.RunAsync(config, context, cancellationToken);
        switch (result)
        {
            case InitializeSuccess:
                await Console.Error.WriteLineAsync(NextStepsGuidance);
                return ExitCodes.Success;
            case InitializeFailure failure:
                await Console.Error.WriteLineAsync($"error: {failure.Error}");
                return ExitCodes.SetupFailed;
            default:
                throw new NotSupportedException($"Unexpected initialize result type: {result.GetType()}");
        }
    }

    /// <summary>The steps <c>rix initialize</c> can't do itself: it only writes files, so setting
    /// secrets, naming the watched CI workflow, and committing are left to the user.</summary>
    private const string NextStepsGuidance = """

        Next steps:
          1. Add repo secrets (Settings -> Secrets and variables -> Actions):
               RIX_READ_TOKEN   PAT, contents:read  (+ actions:read for the CI-failure workflow)
               RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write
          2. In rix-on-ci-failure.yml, set workflows: ["CI"] to your CI workflow's name.
          3. Commit and push the two workflow files.
        """;
}
