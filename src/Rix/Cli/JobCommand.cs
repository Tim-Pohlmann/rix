using System.CommandLine;
using Rix.Job;

namespace Rix.Cli;

internal static class JobCommand
{
    private static readonly Option<string> RepoOption = new(
        name: "--repo",
        description: "Full GitHub repo identifier (owner/repo)")
    { IsRequired = false };

    private static readonly Option<string> PromptOption = new(
        name: "--prompt",
        description: "Task prompt passed to the coding agent")
    { IsRequired = false };

    private static readonly Option<string> ReadTokenOption = new(
        name: "--read-token",
        description: "GitHub PAT with read-only repo access")
    { IsRequired = false };

    private static readonly Option<int?> MaxTokensOption = new(
        name: "--max-tokens",
        description: $"Coding agent token budget cap (default: {JobConfig.DefaultMaxTokens})")
    { IsRequired = false };

    private static readonly Option<int?> TimeoutOption = new(
        name: "--timeout",
        description: $"Wall-clock timeout in minutes (default: {JobConfig.DefaultTimeoutMinutes})")
    { IsRequired = false };

    private static readonly Option<string?> WorkDirOption = new(
        name: "--work-dir",
        description: "Base directory for the temp clone (default: system temp)")
    { IsRequired = false };

    private static readonly Option<string?> OutputDirOption = new(
        name: "--output-dir",
        description: "Directory where result.json and git bundles are written")
    { IsRequired = false };

    private static readonly Option<string?> AgentOption = new(
        name: "--agent",
        description: "Coding agent to run: 'claude' (default) or 'opencode'")
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

        command.SetHandler(async ctx =>
        {
            string Str(Option<string> opt, string env) =>
                ctx.ParseResult.GetValueForOption(opt) ?? Environment.GetEnvironmentVariable(env) ?? string.Empty;
            int? Int(Option<int?> opt, string env) =>
                ctx.ParseResult.GetValueForOption(opt) ?? (int.TryParse(Environment.GetEnvironmentVariable(env), out var n) ? n : null);

            JobConfig config;
            try
            {
                config = JobConfig.FromInputs(
                    repo:    Str(RepoOption,      "RIX_REPO"),
                    prompt:  Str(PromptOption,    "RIX_PROMPT"),
                    readToken: Str(ReadTokenOption, "RIX_READ_TOKEN"),
                    options: new JobInputOptions(
                        MaxTokens:      Int(MaxTokensOption, "RIX_MAX_TOKENS"),
                        TimeoutMinutes: Int(TimeoutOption,   "RIX_TIMEOUT"),
                        WorkDir:        ctx.ParseResult.GetValueForOption(WorkDirOption)
                                        ?? Environment.GetEnvironmentVariable("RIX_WORK_DIR"),
                        OutputDir:      ctx.ParseResult.GetValueForOption(OutputDirOption)
                                        ?? Environment.GetEnvironmentVariable("RIX_OUTPUT_DIR"),
                        Agent:          ctx.ParseResult.GetValueForOption(AgentOption)
                                        ?? Environment.GetEnvironmentVariable("RIX_AGENT")));
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                ctx.ExitCode = ExitCodes.SetupFailed;
                return;
            }
            ctx.ExitCode = await handler(config);
        });

        return command;
    }
}
