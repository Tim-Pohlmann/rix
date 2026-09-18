using Rix.CiFailure;
using System.CommandLine;

namespace Rix.Cli;

internal static class CiFailureCommand
{
    internal static Command Build(Func<CiFailureConfig, Task<int>> handler)
    {
        var command = new Command("ci-failure", "Check whether a workflow run failed and, if so, print a prompt describing it");

        command.AddOption(CiFailureOptions.RepoOption);
        command.AddOption(CiFailureOptions.RunIdOption);
        command.AddOption(CiFailureOptions.ReadTokenOption);

        command.SetHandler
        (
            async ctx => ctx.ExitCode = await handler(CiFailureOptions.ReadConfig(ctx.ParseResult))
        );

        return command;
    }
}
