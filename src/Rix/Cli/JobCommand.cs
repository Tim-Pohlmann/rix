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

    private static readonly Option<string> WorkDirOption = new(
        name: "--work-dir",
        description: "Base directory for the temp clone (default: system temp)")
    { IsRequired = false };

    private static readonly Option<string> OutputDirOption = new(
        name: "--output-dir",
        description: "Directory where result.json and git bundles are written")
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

        command.SetHandler(async ctx =>
        {
            string Str(Option<string> opt, string env) =>
                ctx.ParseResult.GetValueForOption(opt) ?? Environment.GetEnvironmentVariable(env) ?? string.Empty;
            int? Int(Option<int?> opt, string env) =>
                ctx.ParseResult.GetValueForOption(opt) ?? (int.TryParse(Environment.GetEnvironmentVariable(env), out var n) ? n : null);

            var config = JobConfig.FromInputs(
                repo:           Str(RepoOption,      "RIX_REPO"),
                prompt:         Str(PromptOption,    "RIX_PROMPT"),
                readToken:      Str(ReadTokenOption, "RIX_READ_TOKEN"),
                maxTokens:      Int(MaxTokensOption, "RIX_MAX_TOKENS"),
                timeoutMinutes: Int(TimeoutOption,   "RIX_TIMEOUT"),
                workDir:        Str(WorkDirOption,   "RIX_WORK_DIR"),
                outputDir:      Str(OutputDirOption, "RIX_OUTPUT_DIR"));

            ctx.ExitCode = await handler(config);
        });

        return command;
    }
}
