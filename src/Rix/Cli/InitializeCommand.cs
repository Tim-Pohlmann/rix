using Rix.Initialize;
using System.CommandLine;

namespace Rix.Cli;

internal static class InitializeCommand
{
    // Recursive: `rix init factory` writes into a target directory at a ref just as `rix init`
    // does, so both options apply to the subcommand too.
    private static readonly Option<string> DirOption = new("--dir")
    {
        Recursive = true,
        Description = "Target repo directory to write the workflow files into (default: current directory)"
    };

    private static readonly Option<string> RefOption = new("--ref")
    {
        Recursive = true,
        Description = $"Ref of the rix repo the written workflows call, i.e. what follows '@' in their uses: lines (default: {WorkflowRef.ForThisBuild}, the major-version tag of this binary's release)"
    };

    // Not recursive: a factory answers the failures its served repos dispatch, so it watches no
    // workflow of its own.
    private static readonly Option<string> CiWorkflowOption = new("--ci-workflow")
    {
        Description = $"The name: of the workflow whose failures rix answers (default: {CiWorkflowName.Default})"
    };

    /// <summary>A subcommand rather than a flag: a factory is a different kind of repo, not a
    /// variation of the one <c>rix init</c> sets up.</summary>
    private static readonly Command FactoryCommand = new("factory", "Write the workflows of a factory repo, which runs rix against the repos it serves");

    internal static Command Build()
    {
        var command = new Command("initialize", "Write the rix caller workflows into a cloned repo's .github/workflows/");
        // `init` alias: the short form nearly everyone reaches for first.
        command.Aliases.Add("init");
        command.Options.Add(DirOption);
        command.Options.Add(RefOption);
        command.Options.Add(CiWorkflowOption);
        command.Subcommands.Add(FactoryCommand);

        return command;
    }

    internal static InitializeConfig CreateConfig(ParseResult parsed, IFileSystem fileSystem)
    {
        // Unlike the CI-run commands, `initialize` is a local dev step - no RIX_* env fallback; an
        // absent --dir just means "this repo".
        var dir = parsed.GetValue(DirOption) ?? fileSystem.CurrentDirectory;
        return new InitializeConfig
        (
            Input.Required(DirOption.Name, dir, path => new DirectoryPath(path, fileSystem)),
            Setup(parsed)
        );
    }

    /// <summary>Which of the commands <see cref="Build"/> returns was invoked: they share one
    /// action (see <see cref="Startup"/>), and this is where they part.</summary>
    private static IInitializeSetup Setup(ParseResult parsed)
    {
        var reference = Input.Optional(RefOption.Name, parsed.GetValue(RefOption), value => new WorkflowRef(value), WorkflowRef.ForThisBuild);
        if (parsed.CommandResult.Command == FactoryCommand)
            return new FactorySetup(reference);
        var ciWorkflow = Input.Optional(CiWorkflowOption.Name, parsed.GetValue(CiWorkflowOption), value => new CiWorkflowName(value), CiWorkflowName.Default);
        return new StandaloneSetup(reference, ciWorkflow);
    }

    /// <summary>Runs <paramref name="config"/> against the production context. Separate from
    /// <see cref="ExecuteAsync"/> so a test supplies a context of its own and never reaches
    /// <see cref="Composition"/>.</summary>
    internal static Task<int> ExecuteDefaultAsync(InitializeConfig config, CancellationToken cancellationToken)
    => ExecuteAsync(config, DefaultContext(), cancellationToken);

    /// <summary>What writing the templates runs against in production: the disk, and the log the
    /// written paths are reported on.</summary>
    internal static InitializeContext DefaultContext()
    => new(Composition.FileSystem(), Composition.Log());

    /// <summary>
    /// Imperative shell around <see cref="InitializeRunner.RunAsync"/>: prints the error on failure
    /// and maps the result to an exit code.
    /// </summary>
    internal static async Task<int> ExecuteAsync(InitializeConfig config, InitializeContext context, CancellationToken cancellationToken)
    {
        var result = await InitializeRunner.RunAsync(config, context, cancellationToken);
        switch (result)
        {
            case InitializeSuccess:
                return ExitCodes.Success;
            case InitializeFailure failure:
                await Console.Error.WriteLineAsync($"error: {failure.Error}");
                return ExitCodes.SetupFailed;
            default:
                throw new NotSupportedException($"Unexpected initialize result type: {result.GetType()}");
        }
    }
}
