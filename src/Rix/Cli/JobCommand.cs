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
        description: "Task prompt passed to Claude Code")
    { IsRequired = false };

    private static readonly Option<string> ReadTokenOption = new(
        name: "--read-token",
        description: "GitHub PAT with read-only repo access")
    { IsRequired = false };

    private static readonly Option<string> WriteTokenOption = new(
        name: "--write-token",
        description: "GitHub PAT with write access (push + PR creation)")
    { IsRequired = false };

    private static readonly Option<int?> MaxTokensOption = new(
        name: "--max-tokens",
        description: $"Claude Code token budget cap (default: {JobConfig.DefaultMaxTokens})")
    { IsRequired = false };

    private static readonly Option<int?> TimeoutOption = new(
        name: "--timeout",
        description: $"Wall-clock timeout in minutes (default: {JobConfig.DefaultTimeoutMinutes})")
    { IsRequired = false };

    private static readonly Option<string?> WorkDirOption = new(
        name: "--work-dir",
        description: "Base directory for the temp clone (default: system temp)")
    { IsRequired = false };

    internal static Command Build(Func<JobConfig, Task<int>> handler)
    {
        var command = new Command("job", "Clone a repo, run Claude Code against it, and report created PRs");

        command.AddOption(RepoOption);
        command.AddOption(PromptOption);
        command.AddOption(ReadTokenOption);
        command.AddOption(WriteTokenOption);
        command.AddOption(MaxTokensOption);
        command.AddOption(TimeoutOption);
        command.AddOption(WorkDirOption);

        command.SetHandler(async ctx =>
        {
            string Str(Option<string> opt, string env) =>
                ctx.ParseResult.GetValueForOption(opt) ?? Environment.GetEnvironmentVariable(env) ?? string.Empty;
            int? Int(Option<int?> opt, string env) =>
                ctx.ParseResult.GetValueForOption(opt) ?? TryParseInt(Environment.GetEnvironmentVariable(env));

            var config = JobConfig.FromInputs(
                repo:           Str(RepoOption,       "RIX_REPO"),
                prompt:         Str(PromptOption,     "RIX_PROMPT"),
                readToken:      Str(ReadTokenOption,  "RIX_READ_TOKEN"),
                writeToken:     Str(WriteTokenOption, "RIX_WRITE_TOKEN"),
                maxTokens:      Int(MaxTokensOption,  "RIX_MAX_TOKENS"),
                timeoutMinutes: Int(TimeoutOption,    "RIX_TIMEOUT"),
                workDir:        ctx.ParseResult.GetValueForOption(WorkDirOption)
                                ?? Environment.GetEnvironmentVariable("RIX_WORK_DIR"));
            ctx.ExitCode = await handler(config);
        });

        return command;
    }

    private static int? TryParseInt(string? value) =>
        int.TryParse(value, out var n) ? n : null;
}
