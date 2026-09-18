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
        command.AddOption(JobOptions.AgentApiKeyOption);
        command.AddOption(JobOptions.AgentApiKeyEnvOption);

        command.SetHandler
        (
            async ctx =>
            {
                var ciFailure = CiFailureOptions.ReadConfig(ctx.ParseResult);
                // The prompt is a placeholder and the /push allow-list empty on purpose: both are
                // derived by CiFailureJobRunner from the failure it detects, never taken from the
                // caller (see CiFailureJobConfig).
                var job = JobOptions.ReadConfig
                (
                    ctx.ParseResult,
                    repo: ciFailure.Repo,
                    readToken: ciFailure.ReadToken,
                    prompt: CiFailureJobConfig.PlaceholderPrompt,
                    allowedPushBranches: []
                );
                ctx.ExitCode = await handler(new CiFailureJobConfig(ciFailure, job));
            }
        );

        return command;
    }
}
