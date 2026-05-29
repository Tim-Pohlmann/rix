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
            var repo = ctx.ParseResult.GetValueForOption(RepoOption)
                       ?? Environment.GetEnvironmentVariable("RIX_REPO")
                       ?? string.Empty;

            var prompt = ctx.ParseResult.GetValueForOption(PromptOption)
                         ?? Environment.GetEnvironmentVariable("RIX_PROMPT")
                         ?? string.Empty;

            var readToken = ctx.ParseResult.GetValueForOption(ReadTokenOption)
                            ?? Environment.GetEnvironmentVariable("RIX_READ_TOKEN")
                            ?? string.Empty;

            var writeToken = ctx.ParseResult.GetValueForOption(WriteTokenOption)
                             ?? Environment.GetEnvironmentVariable("RIX_WRITE_TOKEN")
                             ?? string.Empty;

            var maxTokens = ctx.ParseResult.GetValueForOption(MaxTokensOption)
                            ?? TryParseInt(Environment.GetEnvironmentVariable("RIX_MAX_TOKENS"));

            var timeout = ctx.ParseResult.GetValueForOption(TimeoutOption)
                          ?? TryParseInt(Environment.GetEnvironmentVariable("RIX_TIMEOUT"));

            var workDir = ctx.ParseResult.GetValueForOption(WorkDirOption)
                          ?? Environment.GetEnvironmentVariable("RIX_WORK_DIR");

            var config = JobConfig.FromInputs(repo, prompt, readToken, writeToken, maxTokens, timeout, workDir);
            ctx.ExitCode = await handler(config);
        });

        return command;
    }

    private static int? TryParseInt(string? value) =>
        int.TryParse(value, out var n) ? n : null;
}
