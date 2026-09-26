using Rix.Initialize;
using System.CommandLine;

namespace Rix.Cli;

internal static class InitializeCommand
{
    private static readonly Option<string> DirOption = new
    (
        name: "--dir",
        description: "Target repo directory to write the workflow files into (default: current directory)"
    )
    { IsRequired = false };

    private static readonly Option<string> RefOption = new
    (
        name: "--ref",
        description: $"Ref of the rix repo the written workflows call, i.e. what follows '@' in their uses: lines (default: {WorkflowRef.ForThisBuild}, the major-version tag of this binary's release)"
    )
    { IsRequired = false };

    internal static Command Build(Func<InitializeConfig, IReadOnlyList<RequiredDirectory>, Task<int>> handler)
    {
        var command = new Command("initialize", "Write the rix caller workflows into a cloned repo's .github/workflows/");
        // `init` alias: the short form nearly everyone reaches for first.
        command.AddAlias("init");
        command.AddOption(DirOption);
        command.AddOption(RefOption);

        command.SetHandler
        (
            async ctx =>
            {
                // Unlike the CI-run commands, `initialize` is a local dev step - no RIX_* env
                // fallback; an absent --dir just means "this repo", i.e. the directory rix runs in,
                // which DirectoryPath resolves "." to.
                var dir = ctx.ParseResult.GetValueForOption(DirOption) ?? ".";
                var config = new InitializeConfig
                (
                    Input.Required(ParseResultExtensions.Flag(DirOption), dir, path => new DirectoryPath(path)),
                    Input.Optional("--ref", ctx.ParseResult.GetValueForOption(RefOption), value => new WorkflowRef(value), WorkflowRef.ForThisBuild)
                );
                ctx.ExitCode = await handler(config, [RequiredDirectory.For(DirOption, config.TargetDir)]);
            }
        );

        return command;
    }
}
