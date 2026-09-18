using Rix.CiFailure;
using System.CommandLine;

namespace Rix.Cli;

internal static class CiFailureCommand
{
    /// <summary>The one option this command adds on top of <see cref="JobOptions"/>, kept private
    /// for the same reason <c>job</c> keeps <c>--prompt</c> to itself: no other command takes
    /// it.</summary>
    private static readonly Option<string> RunIdOption = new
    (
        name: "--run-id",
        description: "ID of the (possibly failed) workflow run to inspect"
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

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                // Same order as `job` for the shared options, so both commands report the same
                // first problem for the same mistake; --run-id comes last as the one addition.
                var repo = JobOptions.ReadRepo(parsed);
                var readToken = JobOptions.ReadReadToken(parsed);
                var agent = JobOptions.ReadAgent(parsed);
                var maxTokens = JobOptions.ReadMaxTokens(parsed);
                var timeout = JobOptions.ReadTimeout(parsed);
                var workDir = JobOptions.ReadWorkDir(parsed);
                var outputDir = JobOptions.ReadOutputDir(parsed);
                var model = JobOptions.ReadModel(parsed);
                var apiKey = JobOptions.ReadAgentApiKey(parsed);
                var apiKeyEnv = JobOptions.ReadAgentApiKeyEnv(parsed, agent, apiKey);
                var runId = Input.Required("--run-id", parsed.Str(RunIdOption, "RIX_RUN_ID"), raw => new RunId(Input.Positive<long>(raw)));

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
