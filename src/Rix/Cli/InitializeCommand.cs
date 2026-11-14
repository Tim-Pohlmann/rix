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

    private static readonly Option<bool> FactoryOption = new("--factory")
    {
        Description = "Set up a factory repo, which runs rix against the repos it serves, instead of a repo that runs rix on itself"
    };

    internal static Command Build()
    {
        var command = new Command("initialize", "Write the rix caller workflows into a cloned repo's .github/workflows/");
        // `init` alias: the short form nearly everyone reaches for first.
        command.Aliases.Add("init");
        command.Options.Add(DirOption);
        command.Options.Add(RefOption);
        command.Options.Add(FactoryOption);

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
            Input.Optional(RefOption.Name, parsed.GetValue(RefOption), value => new WorkflowRef(value), WorkflowRef.ForThisBuild),
            Setup(parsed)
        );
    }

    private static IInitializeSetup Setup(ParseResult parsed)
    {
        if (parsed.GetValue(FactoryOption))
            return new FactorySetup();
        return new StandaloneSetup();
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
    /// Imperative shell around <see cref="InitializeRunner.RunAsync"/>: writes the caller
    /// workflows, then prints the manual follow-up steps of the configured setup on success, or the
    /// error on failure, and maps the result to an exit code.
    /// </summary>
    internal static async Task<int> ExecuteAsync(InitializeConfig config, InitializeContext context, CancellationToken cancellationToken)
    {
        var result = await InitializeRunner.RunAsync(config, context, cancellationToken);
        switch (result)
        {
            case InitializeSuccess:
                await Console.Error.WriteLineAsync(NextSteps(config.Setup));
                return ExitCodes.Success;
            case InitializeFailure failure:
                await Console.Error.WriteLineAsync($"error: {failure.Error}");
                return ExitCodes.SetupFailed;
            default:
                throw new NotSupportedException($"Unexpected initialize result type: {result.GetType()}");
        }
    }

    /// <summary>The steps <c>rix initialize</c> can't do itself: it only writes files, so setting
    /// up the secret and the environment that holds the write token, and committing, are left to
    /// the user, along with whatever else <paramref name="setup"/> needs.</summary>
    internal static string NextSteps(IInitializeSetup setup) => setup switch
    {
        StandaloneSetup => StandaloneNextSteps,
        FactorySetup => FactoryNextSteps,
        _ => throw new NotSupportedException($"Unexpected initialize setup type: {setup.GetType()}"),
    };

    /// <summary>Also names the watched CI workflow, which only a repo answering its own failures
    /// has.</summary>
    private const string StandaloneNextSteps = """

        Next steps:
          1. Add the read token as a repo secret (Settings -> Secrets and variables -> Actions):
               RIX_READ_TOKEN   PAT, contents:read  (+ actions:read for the CI-failure workflow)
          2. Add the write token to an environment named 'rix' (Settings -> Environments), and
             limit that environment's deployment branches to the ones these workflows run on:
               RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write
          3. In rix-on-ci-failure.yml, set workflows: ["CI"] to your CI workflow's name.
          4. Commit and push the two workflow files.
        """;

    /// <summary>The tokens span every served repo, and the write token's reach is what bounds the
    /// factory, so its scope is spelled out. The served repos still need a trigger of their own,
    /// since a workflow_run cannot cross repos.</summary>
    private const string FactoryNextSteps = """

        Next steps:
          1. Add the read token as a repo secret (Settings -> Secrets and variables -> Actions):
               RIX_READ_TOKEN   PAT, contents:read + actions:read on every repo this factory serves
          2. Add the write token to an environment named 'rix' (Settings -> Environments), and
             limit that environment's deployment branches to this repo's default branch:
               RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write on exactly the repos
                                this factory serves; it bounds where rix can open PRs
          3. Commit and push the two workflow files.
          4. In each repo this factory serves, add the workflow that dispatches its CI failures
             here (README: "Advanced: a central factory repo").
        """;
}
