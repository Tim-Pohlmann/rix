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

    internal static Command Build(Func<InitializeConfig, Task<int>> handler)
    {
        var command = new Command("initialize", "Write the rix caller workflows into a cloned repo's .github/workflows/");
        // `init` alias: the short form nearly everyone reaches for first.
        command.AddAlias("init");
        command.AddOption(DirOption);

        command.SetHandler
        (
            async ctx =>
            {
                // Unlike the CI-run commands, `initialize` is a local dev step - no RIX_* env
                // fallback; an absent --dir just means "this repo".
                var dir = ctx.ParseResult.GetValueForOption(DirOption) ?? Directory.GetCurrentDirectory();
                var result = InitializeConfig.Create(dir);

                switch (result)
                {
                    case InitializeConfigValid valid:
                        ctx.ExitCode = await handler(valid.Config);
                        break;
                    case InitializeConfigInvalid invalid:
                        foreach (var error in invalid.Errors)
                            Console.Error.WriteLine($"error: {error}");
                        ctx.ExitCode = ExitCodes.SetupFailed;
                        break;
                    default:
                        throw new NotSupportedException($"Unexpected config result: {result.GetType()}");
                }
            }
        );

        return command;
    }
}
