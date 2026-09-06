using Rix.Job;
using System.CommandLine;

namespace Rix.Cli;

internal static class JobCommand
{
    private static readonly Option<string> RepoOption = new
    (
        name: "--repo",
        description: "Full GitHub repo identifier (owner/repo)"
    )
    { IsRequired = false };

    private static readonly Option<string> PromptOption = new
    (
        name: "--prompt",
        description: "Task prompt passed to the coding agent"
    )
    { IsRequired = false };

    private static readonly Option<string> ReadTokenOption = new
    (
        name: "--read-token",
        description: "GitHub PAT with read-only repo access"
    )
    { IsRequired = false };

    internal static Command Build(Func<JobConfig, Task<int>> handler)
    {
        var command = new Command("job", "Clone a repo, run a coding agent against it, and write output bundles");

        command.AddOption(RepoOption);
        command.AddOption(PromptOption);
        command.AddOption(ReadTokenOption);
        command.AddOption(JobOptions.MaxTokensOption);
        command.AddOption(JobOptions.TimeoutOption);
        command.AddOption(JobOptions.WorkDirOption);
        command.AddOption(JobOptions.OutputDirOption);
        command.AddOption(JobOptions.AgentOption);
        command.AddOption(JobOptions.ModelOption);
        command.AddOption(JobOptions.AgentApiKeyOption);
        command.AddOption(JobOptions.AgentApiKeyEnvOption);
        command.AddOption(JobOptions.AllowedPushBranchesOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                var inputs = new JobInputs
                (
                    Repo:           parsed.Str(RepoOption,      "RIX_REPO"),
                    Prompt:         parsed.Str(PromptOption,    "RIX_PROMPT"),
                    ReadToken:      parsed.Str(ReadTokenOption, "RIX_READ_TOKEN"),
                    MaxTokens:      parsed.Str(JobOptions.MaxTokensOption, "RIX_MAX_TOKENS"),
                    TimeoutMinutes: parsed.Str(JobOptions.TimeoutOption,   "RIX_TIMEOUT"),
                    WorkDir:        parsed.Str(JobOptions.WorkDirOption,   "RIX_WORK_DIR"),
                    OutputDir:      parsed.Str(JobOptions.OutputDirOption, "RIX_OUTPUT_DIR"),
                    Agent:          parsed.Str(JobOptions.AgentOption,     "RIX_AGENT"),
                    Model:          parsed.Str(JobOptions.ModelOption,     "RIX_MODEL"),
                    AgentApiKey:    parsed.Str(JobOptions.AgentApiKeyOption,    "AGENT_API_KEY"),
                    AgentApiKeyEnv: parsed.Str(JobOptions.AgentApiKeyEnvOption, "AGENT_API_KEY_ENV"),
                    AllowedPushBranches: parsed.Str(JobOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES")
                );
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
