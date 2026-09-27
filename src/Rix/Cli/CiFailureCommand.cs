using Rix.CiFailure;
using System.CommandLine;

namespace Rix.Cli;

internal static class CiFailureCommand
{
    /// <summary>The two options this command adds on top of <see cref="JobOptions"/>, kept private
    /// for the same reason <c>job</c> keeps <c>--prompt</c> to itself: no other command takes
    /// them.</summary>
    private static readonly Option<string> RunIdOption = new("--run-id")
    {
        Description = "ID of the (possibly failed) workflow run to inspect"
    };

    private static readonly Option<string> MaxRixCommitsOption = new("--max-rix-commits")
    {
        Description = "How many of rix's own commits may already sit at the failing branch's tip before " +
            "the failure is left alone instead of answered. Stops rix from answering its own output " +
            "indefinitely; any commit by someone else at the tip clears the count. Defaults to " +
            $"{CiFailureConfig.DefaultMaxRixCommits}, at most {MaxRixCommits.MaxValue}."
    };

    internal static Command Build()
    {
        var command = new Command
        (
            "ci-failure",
            "Check whether a workflow run failed and, if so, run a coding agent against the failure"
        );

        JobOptions.AddTo(command);
        command.Options.Add(RunIdOption);
        command.Options.Add(MaxRixCommitsOption);

        return command;
    }

    internal static CiFailureConfig ReadConfig(ParseResult parsed)
    {
        // Same order as `job` for the shared options, so both commands report the same first
        // problem for the same mistake; this command's own two come last.
        var repo = CommonOptions.ReadRepo(parsed);
        var readToken = JobOptions.ReadReadToken(parsed);
        var agent = JobOptions.ReadAgent(parsed);
        var maxTokens = JobOptions.ReadMaxTokens(parsed);
        var timeout = JobOptions.ReadTimeout(parsed);
        var workDir = CommonOptions.ReadWorkDir(parsed);
        var outputDir = JobOptions.ReadOutputDir(parsed);
        var model = JobOptions.ReadModel(parsed);
        var apiKey = JobOptions.ReadAgentApiKey(parsed);
        var credential = JobOptions.ReadAgentCredential(parsed, agent, apiKey);
        var runId = parsed.Required(RunIdOption, "RIX_RUN_ID", raw => new RunId(Input.WholeNumber<long>(raw)));
        var maxRixCommits = parsed.Optional
        (
            MaxRixCommitsOption,
            "RIX_MAX_RIX_COMMITS",
            raw => new MaxRixCommits(Input.WholeNumber<int>(raw)),
            new MaxRixCommits(CiFailureConfig.DefaultMaxRixCommits)
        );

        return new CiFailureConfig
        (
            runId,
            repo,
            readToken,
            timeout,
            WorkDir: workDir,
            OutputDir: outputDir,
            agent,
            maxTokens,
            maxRixCommits,
            model,
            credential
        );
    }
}
