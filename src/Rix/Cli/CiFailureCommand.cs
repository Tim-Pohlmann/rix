using Rix.CiFailure;
using System.CommandLine;

namespace Rix.Cli;

internal static class CiFailureCommand
{
    /// <summary>The two options this command adds on top of <see cref="JobOptions"/>, kept private
    /// for the same reason <c>job</c> keeps <c>--prompt</c> to itself: no other command takes
    /// them.</summary>
    private static readonly Option<string> RunIdOption = new
    (
        name: "--run-id",
        description: "ID of the (possibly failed) workflow run to inspect"
    )
    { IsRequired = false };

    private static readonly Option<string> MaxRixCommitsOption = new
    (
        name: "--max-rix-commits",
        description: "How many of rix's own commits may already sit at the failing branch's tip before " +
            "the failure is left alone instead of answered. Stops rix from answering its own output " +
            "indefinitely; any commit by someone else at the tip clears the count. Defaults to " +
            $"{CiFailureConfig.DefaultMaxRixCommits}, at most {MaxRixCommits.MaxValue}."
    )
    { IsRequired = false };

    internal static Command Build(Func<CiFailureConfig, Task<int>> handler)
    {
        var command = new Command
        (
            "ci-failure",
            "Check whether a workflow run failed and, if so, run a coding agent against the failure"
        );

        JobOptions.AddTo(command);
        command.AddOption(RunIdOption);
        command.AddOption(MaxRixCommitsOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                // Same order as `job` for the shared options, so both commands report the same
                // first problem for the same mistake; this command's own two come last.
                var repo = CommonOptions.ReadRepo(parsed);
                var readToken = JobOptions.ReadReadToken(parsed);
                var agent = JobOptions.ReadAgent(parsed);
                var maxTokens = JobOptions.ReadMaxTokens(parsed);
                var timeout = JobOptions.ReadTimeout(parsed);
                var workDir = CommonOptions.ReadWorkDir(parsed);
                var outputDir = JobOptions.ReadOutputDir(parsed);
                var model = JobOptions.ReadModel(parsed);
                var apiKey = JobOptions.ReadAgentApiKey(parsed);
                var apiKeyEnv = JobOptions.ReadAgentApiKeyEnv(parsed, agent, apiKey);
                var runId = parsed.Required(RunIdOption, "RIX_RUN_ID", raw => new RunId(Input.Positive<long>(raw)));
                var maxRixCommits = parsed.Optional
                (
                    MaxRixCommitsOption,
                    "RIX_MAX_RIX_COMMITS",
                    raw => new MaxRixCommits(Input.Positive<int>(raw)),
                    new MaxRixCommits(CiFailureConfig.DefaultMaxRixCommits)
                );

                var config = new CiFailureConfig
                (
                    RunId: runId,
                    Repo: repo,
                    ReadToken: readToken,
                    TimeoutMinutes: timeout,
                    WorkDir: workDir,
                    OutputDir: outputDir,
                    Agent: agent,
                    MaxTokens: maxTokens,
                    MaxRixCommits: maxRixCommits,
                    Model: model,
                    ApiKey: apiKey,
                    ApiKeyEnv: apiKeyEnv
                );
                ctx.ExitCode = await handler(config);
            }
        );

        return command;
    }
}
