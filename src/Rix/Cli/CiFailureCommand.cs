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
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                var result = CiFailureConfig.Create
                (
                    repo:      parsed.Str(CiFailureOptions.RepoOption,      "RIX_REPO"),
                    readToken: parsed.Str(CiFailureOptions.ReadTokenOption, "RIX_READ_TOKEN"),
                    runId:     parsed.Str(CiFailureOptions.RunIdOption,     "RIX_RUN_ID")
                );

                switch (result)
                {
                    case CiFailureConfigValid valid:
                        ctx.ExitCode = await handler(valid.Config);
                        break;
                    case CiFailureConfigInvalid invalid:
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
