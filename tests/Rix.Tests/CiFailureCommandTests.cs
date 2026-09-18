using Rix.Cli;
using Rix.CiFailure;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class CiFailureCommandTests
{
    private static Parser BuildParser(Func<CiFailureConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(CiFailureCommand.Build(handler));
        return new CommandLineBuilder(root).UseDefaults().Build();
    }

    /// <summary>The job half of a parsed config, with the two values only a detected failure can
    /// supply stubbed out — these tests assert on what came off the command line, not on those.</summary>
    private static JobConfig Job(CiFailureConfig config)
    => config.ToJobConfig("fix it", new BranchName("rix/fix"));

    [TestMethod]
    public async Task Command_PassesEnvVarFallbacks_WhenFlagsAbsent()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        env.Set("RIX_READ_TOKEN", "env-read");
        env.Set("RIX_RUN_ID", "42");
        env.Set("RIX_MAX_TOKENS", "999");
        env.Set("RIX_TIMEOUT", "15");
        env.Set("RIX_WORK_DIR", Path.GetTempPath());
        env.Set("RIX_OUTPUT_DIR", Path.GetTempPath());
        await parser.InvokeAsync("ci-failure");

        Assert.IsNotNull(captured);
        var job = Job(captured);
        Assert.AreEqual("env/repo", captured.Repo.ToString());
        Assert.AreEqual("env-read", captured.ReadToken.Value);
        Assert.AreEqual(42, captured.RunId.Value);
        Assert.AreEqual(999, job.Agent.MaxTokens.Value);
        Assert.AreEqual(15, job.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), job.WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), job.OutputDir.Value);
    }

    [TestMethod]
    public async Task Command_FlagsTakePrecedenceOverEnvVars()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        await parser.InvokeAsync(
            ["ci-failure", "--repo", "flag/repo", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath()]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("flag/repo", captured.Repo.ToString());
    }

    [TestMethod]
    public async Task Command_SelectsAgent_FromFlag()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--agent", "opencode"]);

        Assert.IsNotNull(captured);
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, Job(captured).Agent.Kind);
    }

    [TestMethod]
    public async Task Command_PassesThroughModel_FromFlag()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--model", "openai/gpt-4o"]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("openai/gpt-4o", Job(captured).Agent.Model);
    }

    [TestMethod]
    public async Task Command_PassesThroughAgentApiKeyAndEnv_FromFlags()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--agent-api-key", "secret", "--agent-api-key-env", "ANTHROPIC_API_KEY"]);

        Assert.IsNotNull(captured);
        var agent = Job(captured).Agent;
        Assert.AreEqual("secret", agent.ApiKey);
        Assert.AreEqual("ANTHROPIC_API_KEY", agent.ApiKeyEnv);
    }
}
