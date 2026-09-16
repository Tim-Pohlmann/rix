using Rix.CiFailure;
using Rix.Job;
using System.CommandLine;

namespace Rix.Cli;

internal static class CiFailureCommand
{
    /// <summary>The one option this command adds on top of <see cref="JobOptions"/>, kept private
    /// for the same reason <c>job</c> keeps <c>--prompt</c> to itself: no other command takes
    /// it.</summary>
    private static readonly Option<string> RunIdOption = new
    (
        name: "--run-id",
        description: "ID of the (possibly failed) workflow run to inspect"
    )
    { IsRequired = false };

    internal static Command Build(Func<CiFailureConfig, Task<int>> handler)
    {
        var command = new Command
        (
            "ci-failure",
            "Check whether a workflow run failed and, if so, run a coding agent against the failure"
        );

        JobOptions.AddTo(command);
        command.AddOption(RunIdOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                var inputs = new CiFailureInputs
                (
                    RunId: parsed.Str(RunIdOption, "RIX_RUN_ID"),
                    Job: JobOptions.ReadInputs(parsed)
                );
                var result = CiFailureConfig.Create(inputs);

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
