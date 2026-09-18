using Rix.Cli;
using Rix.CiFailure;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class CiFailureCommandTests
{
    private static Parser BuildParser(Func<CiFailureConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(CiFailureCommand.Build(handler));
        return CliPipeline.Build(root);
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

    [TestMethod]
    public async Task Command_DefaultsMaxRixCommits_WhenFlagAndEnvAbsent()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1", "--output-dir", Path.GetTempPath()]);

        Assert.AreEqual(CiFailureConfig.DefaultMaxRixCommits, captured?.MaxRixCommits.Value);
    }

    [TestMethod]
    public async Task Command_PassesThroughMaxRixCommits_FromFlag()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--max-rix-commits", "2"]);

        Assert.AreEqual(2, captured?.MaxRixCommits.Value);
    }

    [TestMethod]
    [DataRow("abc", "error: --max-rix-commits: must be a positive integer, got 'abc'")]
    [DataRow("0", "error: --max-rix-commits: must be a positive integer, got '0'")]
    [DataRow("101", "error: --max-rix-commits: must be between 1 and 100, got '101'")]
    public async Task Command_Returns2_WhenMaxRixCommitsIsOutOfRange(string raw, string expectedError)
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(
            ["ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--max-rix-commits", raw]);

        Assert.IsNull(captured);
        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, expectedError);
    }

    [TestMethod]
    public async Task Command_DoesNotTakeAPrompt()
    {
        // The prompt describes a failure that hasn't been detected yet, so the command neither
        // requires nor accepts one - a caller passing it gets the parser's own unknown-option error.
        var parser = BuildParser(_ => Task.FromResult(0));

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(
            ["ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--prompt", "fix it"]);

        Assert.AreNotEqual(ExitCodes.Success, exitCode);
    }

    [TestMethod]
    [DataRow("", "r", "1", "error: --repo is required")]
    [DataRow("noslash", "r", "1", "error: --repo: 'noslash' is not a valid repo identifier")]
    [DataRow("o/r", "", "1", "error: --read-token is required")]
    [DataRow("o/r", "r", "", "error: --run-id is required")]
    [DataRow("o/r", "r", "abc", "error: --run-id: must be a positive integer, got 'abc'")]
    [DataRow("o/r", "r", "0", "error: --run-id: must be a positive integer, got '0'")]
    public async Task Command_Returns2_AndReportsTheFlag_WhenInputInvalid(string repo, string readToken, string runId, string expectedError)
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(
            ["ci-failure", "--repo", repo, "--read-token", readToken, "--run-id", runId, "--output-dir", Path.GetTempPath()]);

        Assert.IsNull(captured);
        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, expectedError);
    }

    [TestMethod]
    public async Task Command_Returns2_WhenASharedJobOptionIsMalformed()
    {
        // The shared job options are validated up front, before the run is even looked at, so a
        // typo surfaces immediately rather than only once a failure has been detected.
        var parser = BuildParser(_ => Task.FromResult(0));

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(
            ["ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
             "--output-dir", Path.GetTempPath(), "--max-tokens", "abc"]);

        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, "error: --max-tokens: must be a positive integer, got 'abc'");
    }

    [TestMethod]
    public async Task Command_ReportsOnlyTheFirstProblem()
    {
        var parser = BuildParser(_ => Task.FromResult(0));

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(["ci-failure", "--repo", "", "--read-token", "", "--run-id", ""]);

        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        Assert.AreEqual("error: --repo is required", stderr.Text.Trim());
    }
}
