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
                var config = JobOptions.ReadConfig
                (
                    parsed,
                    repo: Input.Required("--repo", parsed.Str(RepoOption, "RIX_REPO"), value => new RepoIdentifier(value)),
                    readToken: Input.Required("--read-token", parsed.Str(ReadTokenOption, "RIX_READ_TOKEN"), value => new GitReadToken(value)),
                    prompt: Input.Required("--prompt", parsed.Str(PromptOption, "RIX_PROMPT"), value => value),
                    allowedPushBranches: ParseAllowedPushBranches(parsed.Str(JobOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES"))
                );
                ctx.ExitCode = await handler(config);
            }
        );

        return command;
    }

    /// <summary>Parses the raw comma-separated <c>--allowed-push-branches</c> value into the
    /// branches the <c>/push</c> API endpoint may deliver to. Blank input (the flag was
    /// never set) means <c>/push</c> permits nothing, so the result is the empty list — an operator
    /// must opt in to letting the agent push at all. Unlike the <c>rix/*</c>-restricted branches the
    /// agent creates via <c>/pr</c>, any branch name is acceptable here, since these already exist on
    /// the remote before the job ever runs. Duplicates are dropped.</summary>
    private static List<BranchName> ParseAllowedPushBranches(string raw)
    => raw
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(entry => new BranchName(entry))
        .Distinct()
        .ToList();
}
