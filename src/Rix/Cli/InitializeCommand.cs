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

    /// <summary>What a bare <c>rix init</c> asks for: a factory serves many repos, so a served repo
    /// is the setup most often written.</summary>
    private static readonly Argument<string> FactoryRepoArgument = new("factory-repo")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "The factory repo (owner/name) that serves this one: write only the workflow that dispatches this repo's CI failures to it"
    };

    private static readonly Option<bool> SingleRepoOption = new("--single-repo")
    {
        Description = "Set up a repo that runs rix on itself, with no factory"
    };

    internal static Command Build()
    {
        var command = new Command("initialize", "Write the rix caller workflows into a cloned repo's .github/workflows/");
        // `init` alias: the short form nearly everyone reaches for first.
        command.Aliases.Add("init");
        command.Options.Add(DirOption);
        command.Options.Add(RefOption);
        command.Options.Add(CiWorkflowOption);
        command.Arguments.Add(FactoryRepoArgument);
        command.Options.Add(SingleRepoOption);
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

    /// <summary>Which of the commands <see cref="Build"/> returns was invoked, and with what: they
    /// share one action (see <see cref="Startup"/>), and this is where they part. A repo is the
    /// factory, is served by one, or runs rix on itself, so naming more than one is refused rather
    /// than resolved by precedence.</summary>
    private static IInitializeSetup Setup(ParseResult parsed)
    {
        var reference = Input.Optional(RefOption.Name, parsed.GetValue(RefOption), value => new WorkflowRef(value), WorkflowRef.ForThisBuild);
        var factoryRepo = parsed.GetValue(FactoryRepoArgument);
        var singleRepo = parsed.GetValue(SingleRepoOption);
        if (parsed.CommandResult.Command == FactoryCommand)
        {
            if (singleRepo || !string.IsNullOrWhiteSpace(factoryRepo))
                throw new InvalidInputException($"{FactoryCommand.Name} takes neither a factory repo nor {SingleRepoOption.Name}; it sets up the factory itself");
            return new FactorySetup(reference);
        }
        var ciWorkflow = Input.Optional(CiWorkflowOption.Name, parsed.GetValue(CiWorkflowOption), value => new CiWorkflowName(value), CiWorkflowName.Default);
        if (singleRepo)
        {
            if (!string.IsNullOrWhiteSpace(factoryRepo))
                throw new InvalidInputException($"{SingleRepoOption.Name} and a factory repo cannot be combined; a single repo runs rix on itself");
            return new StandaloneSetup(reference, ciWorkflow);
        }
        if (string.IsNullOrWhiteSpace(factoryRepo))
            throw new InvalidInputException($"name the factory repo that serves this one (rix initialize <owner/name>), pass {SingleRepoOption.Name} for a repo that runs rix on itself, or run rix initialize {FactoryCommand.Name} in the factory repo");
        return Input.Named($"<{FactoryRepoArgument.Name}>", () => new FactoryServedSetup(new RepoIdentifier(factoryRepo), ciWorkflow));
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
                context.Log.WriteLine($"error: {failure.Error}");
                return ExitCodes.SetupFailed;
            default:
                throw new NotSupportedException($"Unexpected initialize result type: {result.GetType()}");
        }
    }
}
