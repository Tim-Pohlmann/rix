using Rix.CiFailure;
using System.CommandLine;

namespace Rix.Cli;

internal static class CiFailureJobCommand
{
    internal static Command Build(Func<CiFailureJobConfig, Task<int>> handler)
    {
        var command = new Command
        (
            "ci-failure-job",
            "Check whether a workflow run failed and, if so, run a coding agent against the failure"
        );

        command.AddOption(CiFailureOptions.RepoOption);
        command.AddOption(CiFailureOptions.RunIdOption);
        command.AddOption(CiFailureOptions.ReadTokenOption);
        command.AddOption(JobOptions.MaxTokensOption);
        command.AddOption(JobOptions.TimeoutOption);
        command.AddOption(JobOptions.WorkDirOption);
        command.AddOption(JobOptions.OutputDirOption);
        command.AddOption(JobOptions.AgentOption);
        command.AddOption(JobOptions.ModelOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                var inputs = new CiFailureJobInputs
                (
                    Repo:           parsed.Str(CiFailureOptions.RepoOption,      "RIX_REPO"),
                    ReadToken:      parsed.Str(CiFailureOptions.ReadTokenOption, "RIX_READ_TOKEN"),
                    RunId:          parsed.Str(CiFailureOptions.RunIdOption,     "RIX_RUN_ID"),
                    MaxTokens:      parsed.Str(JobOptions.MaxTokensOption, "RIX_MAX_TOKENS"),
                    TimeoutMinutes: parsed.Str(JobOptions.TimeoutOption,   "RIX_TIMEOUT"),
                    WorkDir:        parsed.Str(JobOptions.WorkDirOption,   "RIX_WORK_DIR"),
                    OutputDir:      parsed.Str(JobOptions.OutputDirOption, "RIX_OUTPUT_DIR"),
                    Agent:          parsed.Str(JobOptions.AgentOption,     "RIX_AGENT"),
                    Model:          parsed.Str(JobOptions.ModelOption,     "RIX_MODEL")
                );
                var result = CiFailureJobConfig.Create(inputs);

                switch (result)
                {
                    case CiFailureJobConfigValid valid:
                        ctx.ExitCode = await handler(valid.Config);
                        break;
                    case CiFailureJobConfigInvalid invalid:
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
