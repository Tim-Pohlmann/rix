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

        return command;
    }

    internal static InitializeConfig ReadConfig(ParseResult parsed)
    {
        // Unlike the CI-run commands, `initialize` is a local dev step - no RIX_* env fallback; an
        // absent --dir just means "this repo", i.e. the directory rix runs in, which DirectoryPath
        // resolves "." to.
        var dir = parsed.GetValue(DirOption) ?? ".";
        return new InitializeConfig
        (
            Input.Required(DirOption.Name, dir, path => new DirectoryPath(path)),
            Input.Optional("--ref", parsed.GetValue(RefOption), value => new WorkflowRef(value), WorkflowRef.ForThisBuild)
        );
    }

    internal static IReadOnlyList<RequiredDirectory> RequiredDirectories(InitializeConfig config)
    => [RequiredDirectory.For(DirOption, config.TargetDir)];
}
