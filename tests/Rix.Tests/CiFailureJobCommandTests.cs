using Rix.Cli;
using Rix.CiFailure;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class CiFailureJobCommandTests
{
    private static Parser BuildParser(Func<CiFailureJobConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(CiFailureJobCommand.Build(handler));
        return new CommandLineBuilder(root).UseDefaults().Build();
    }

    [TestMethod]
    public async Task Command_PassesEnvVarFallbacks_WhenFlagsAbsent()
    {
        CiFailureJobConfig? captured = null;
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
        await parser.InvokeAsync("ci-failure-job");

        Assert.IsNotNull(captured);
        Assert.AreEqual("env/repo", captured.ToCiFailureConfig().Repo.ToString());
        Assert.AreEqual("env-read", captured.ToCiFailureConfig().ReadToken.Value);
        Assert.AreEqual(42, captured.ToCiFailureConfig().RunId);
        Assert.AreEqual(999, captured.ToJobConfig().Agent.MaxTokens.Value);
        Assert.AreEqual(15, captured.ToJobConfig().TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.ToJobConfig().WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.ToJobConfig().OutputDir.Value);
    }

    [TestMethod]
    public async Task Command_FlagsTakePrecedenceOverEnvVars()
    {
        CiFailureJobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        await parser.InvokeAsync(
            ["ci-failure-job", "--repo", "flag/repo", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath()]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("flag/repo", captured.ToCiFailureConfig().Repo.ToString());
    }

    [TestMethod]
    public async Task Command_SelectsAgent_FromFlag()
    {
        CiFailureJobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure-job", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--agent", "opencode"]);

        Assert.IsNotNull(captured);
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, captured.ToJobConfig().Agent.Kind);
    }

    [TestMethod]
    public async Task Command_PassesThroughModel_FromFlag()
    {
        CiFailureJobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure-job", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--model", "openai/gpt-4o"]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("openai/gpt-4o", captured.ToJobConfig().Agent.Model);
    }

    [TestMethod]
    public async Task Command_PassesThroughAgentApiKeyAndEnv_FromFlags()
    {
        CiFailureJobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure-job", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--agent-api-key", "secret", "--agent-api-key-env", "ANTHROPIC_API_KEY"]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("secret", captured.ToJobConfig().Agent.ApiKey);
        Assert.AreEqual("ANTHROPIC_API_KEY", captured.ToJobConfig().Agent.ApiKeyEnv);
    }
}
