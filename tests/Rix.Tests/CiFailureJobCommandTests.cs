using Rix.Cli;
using Rix.CiFailure;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class CiFailureJobCommandTests
{
    private static Parser BuildParser(Func<CiFailureJobConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(CiFailureJobCommand.Build(handler));
        return CliPipeline.Build(root);
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
        Assert.AreEqual("env/repo", captured.CiFailure.Repo.ToString());
        Assert.AreEqual("env-read", captured.CiFailure.ReadToken.Value);
        Assert.AreEqual(42, captured.CiFailure.RunId);
        Assert.AreEqual(999, captured.Job.Agent.MaxTokens.Value);
        Assert.AreEqual(15, captured.Job.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.Job.WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.Job.OutputDir.Value);
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
        Assert.AreEqual("flag/repo", captured.CiFailure.Repo.ToString());
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
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, captured.Job.Agent.Kind);
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
        Assert.AreEqual("openai/gpt-4o", captured.Job.Agent.Model);
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
        Assert.AreEqual("secret", captured.Job.Agent.ApiKey);
        Assert.AreEqual("ANTHROPIC_API_KEY", captured.Job.Agent.ApiKeyEnv);
    }

    [TestMethod]
    public async Task Command_UsesPlaceholderPrompt_AndNoAllowedPushBranches()
    {
        CiFailureJobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure-job", "--repo", "o/r", "--read-token", "r", "--run-id", "1", "--output-dir", Path.GetTempPath()]);

        Assert.IsNotNull(captured);
        // Both are derived from the detected failure by CiFailureJobRunner, never from the caller.
        Assert.AreEqual(CiFailureJobConfig.PlaceholderPrompt, captured.Job.Agent.Prompt);
        Assert.AreEqual(0, captured.Job.AllowedPushBranches.Count);
        Assert.AreEqual(captured.CiFailure.Repo, captured.Job.Repo);
        Assert.AreEqual(captured.CiFailure.ReadToken, captured.Job.ReadToken);
    }

    [TestMethod]
    [DataRow("--max-tokens", "abc", "error: --max-tokens: must be a positive integer, got 'abc'")]
    [DataRow("--agent", "not-a-real-agent", "error: --agent: unknown agent 'not-a-real-agent'")]
    [DataRow("--agent-api-key-env", "NOT_CREDENTIAL_SHAPED", "error: --agent-api-key-env: 'NOT_CREDENTIAL_SHAPED' must be")]
    public async Task Command_Returns2_AndReportsTheFlag_WhenInputInvalid(string flag, string value, string expectedError)
    {
        CiFailureJobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(
            ["ci-failure-job", "--repo", "o/r", "--read-token", "r", "--run-id", "1", "--output-dir", Path.GetTempPath(),
             "--agent-api-key", "secret", flag, value]);

        Assert.IsNull(captured);
        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, expectedError);
    }
}
