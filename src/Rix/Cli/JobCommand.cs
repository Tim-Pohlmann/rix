using Rix.Job;
using System.CommandLine;

namespace Rix.Cli;

internal static class JobCommand
{
    internal static Command Build
    (
        string systemTempDirectory, string userHomeDirectory, Func<JobConfig, IReadOnlyList<RequiredDirectory>, Task<int>> handler
    )
    {
        var command = new Command("job", "Clone a repo, run a coding agent against it, and write output bundles");

        JobOptions.AddTo(command);
        command.AddOption(JobOptions.PromptOption);
        command.AddOption(JobOptions.AllowedPushBranchesOption);
        command.AddOption(JobOptions.FactoryRepoOption);
        command.AddOption(JobOptions.AgentHomePathOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                // Read in the order problems should be reported: the first failing read is the
                // one the user sees.
                var repo = CommonOptions.ReadRepo(parsed);
                var prompt = parsed.RequiredText(JobOptions.PromptOption, "RIX_PROMPT");
                var readToken = JobOptions.ReadReadToken(parsed);
                var agent = JobOptions.ReadAgent(parsed);
                var maxTokens = JobOptions.ReadMaxTokens(parsed);
                var timeout = JobOptions.ReadTimeout(parsed);
                var workDir = CommonOptions.ReadWorkDir(parsed, systemTempDirectory);
                var outputDir = JobOptions.ReadOutputDir(parsed);
                var model = JobOptions.ReadModel(parsed);
                var apiKey = JobOptions.ReadAgentApiKey(parsed);
                var credential = JobOptions.ReadAgentCredential(parsed, agent, apiKey);
                var allowedPushBranches = BranchName.ParseAllowList(parsed.Str(JobOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES"));
                var agentHome = JobOptions.ReadAgentHome(parsed, userHomeDirectory);

                var config = new JobConfig
                (
                    repo,
                    readToken,
                    timeout,
                    WorkDir: workDir,
                    OutputDir: outputDir,
                    new AgentConfig(agent, prompt, maxTokens, model, credential),
                    allowedPushBranches,
                    agentHome
                );
                var directories = JobOptions.RequiredDirectories(workDir, outputDir);
                if (agentHome is not null)
                    directories.Add(new RequiredDirectory(JobOptions.RunnerHomeName, agentHome.Home));
                ctx.ExitCode = await handler(config, directories);
            }
        );

        return command;
    }
}
