using Rix.Job;
using System.CommandLine;

namespace Rix.Cli;

internal static class JobCommand
{
    internal static Command Build(Func<JobConfig, Task<int>> handler)
    {
        var command = new Command("job", "Clone a repo, run a coding agent against it, and write output bundles");

        JobOptions.AddTo(command);
        command.AddOption(JobOptions.PromptOption);
        command.AddOption(JobOptions.AllowedPushBranchesOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                var inputs = JobOptions.ReadInputs(parsed) with
                {
                    Prompt = parsed.Str(JobOptions.PromptOption, "RIX_PROMPT"),
                    AllowedPushBranches = parsed.Str(JobOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES"),
                };
                var result = JobConfig.Create(inputs);

                switch (result)
                {
                    case JobConfigValid valid:
                        ctx.ExitCode = await handler(valid.Config);
                        break;
                    case JobConfigInvalid invalid:
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
