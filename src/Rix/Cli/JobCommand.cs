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

    private static readonly Option<string> MaxTokensOption = new
    (
        name: "--max-tokens",
        description: $"Coding agent token budget cap (default: {JobConfig.DefaultMaxTokens})"
    )
    { IsRequired = false };

    private static readonly Option<string> TimeoutOption = new
    (
        name: "--timeout",
        description: $"Wall-clock timeout in minutes (default: {JobConfig.DefaultTimeoutMinutes})"
    )
    { IsRequired = false };

    private static readonly Option<string> WorkDirOption = new
    (
        name: "--work-dir",
        description: "Base directory for the temp clone (default: system temp)"
    )
    { IsRequired = false };

    private static readonly Option<string> OutputDirOption = new
    (
        name: "--output-dir",
        description: "Directory where result.json and git bundles are written"
    )
    { IsRequired = false };

    private static readonly Option<string> AgentOption = new
    (
        name: "--agent",
        description: "Coding agent to run: 'opencode' (default), 'claude', or 'pi'"
    )
    { IsRequired = false };

    private static readonly Option<string> ModelOption = new
    (
        name: "--model",
        description: "Model identifier passed to the agent CLI (e.g. 'openai/gpt-4o' for opencode). " +
            "Provider-specific; forwarded verbatim. Omit to use the agent CLI's own default model."
    )
    { IsRequired = false };

    internal static Command Build(Func<JobConfig, Task<int>> handler)
    {
        var command = new Command("job", "Clone a repo, run a coding agent against it, and write output bundles");

        command.AddOption(RepoOption);
        command.AddOption(PromptOption);
        command.AddOption(ReadTokenOption);
        command.AddOption(MaxTokensOption);
        command.AddOption(TimeoutOption);
        command.AddOption(WorkDirOption);
        command.AddOption(OutputDirOption);
        command.AddOption(AgentOption);
        command.AddOption(ModelOption);

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
                    MaxTokens:      parsed.Str(MaxTokensOption, "RIX_MAX_TOKENS"),
                    TimeoutMinutes: parsed.Str(TimeoutOption,   "RIX_TIMEOUT"),
                    WorkDir:        parsed.Str(WorkDirOption,   "RIX_WORK_DIR"),
                    OutputDir:      parsed.Str(OutputDirOption, "RIX_OUTPUT_DIR"),
                    Agent:          parsed.Str(AgentOption,     "RIX_AGENT"),
                    Model:          parsed.Str(ModelOption,     "RIX_MODEL")
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
