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

    internal static InitializeConfig ReadConfig(ParseResult parsed, IFileSystem fileSystem)
    {
        // Unlike the CI-run commands, `initialize` is a local dev step - no RIX_* env fallback; an
        // absent --dir just means "this repo".
        var dir = parsed.GetValue(DirOption) ?? fileSystem.CurrentDirectory;
        return new InitializeConfig
        (
            Input.Required("--dir", dir, path => new DirectoryPath(path, fileSystem)),
            Input.Optional("--ref", parsed.GetValue(RefOption), value => new WorkflowRef(value), WorkflowRef.ForThisBuild)
        );
    }
}
