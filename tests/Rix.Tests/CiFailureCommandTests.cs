using Rix.Agents;
using Rix.Cli;
using Rix.CiFailure;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class CiFailureCommandTests
{
    private static CiFailureConfig Read(params string[] args)
    => CiFailureCommand.ReadConfig(CommandArgs.Parse(CiFailureCommand.Build(), ["ci-failure", .. args]));

    /// <summary>Reads <c>ci-failure</c> with valid values for every required flag, then
    /// <paramref name="extra"/>.</summary>
    private static CiFailureConfig ReadValid(params string[] extra)
    => Read(["--repo", "o/r", "--read-token", "r", "--run-id", "1", "--output-dir", Path.GetTempPath(), .. extra]);

    /// <summary>The job half of a parsed config, with the two values only a detected failure can
    /// supply stubbed out — these tests assert on what came off the command line, not on those.</summary>
    private static JobConfig Job(CiFailureConfig config)
    => config.ToJobConfig("fix it", new BranchName("rix/fix"));

    [TestMethod]
    public void Command_PassesEnvVarFallbacks_WhenFlagsAbsent()
    {
        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        env.Set("RIX_READ_TOKEN", "env-read");
        env.Set("RIX_RUN_ID", "42");
        env.Set("RIX_MAX_TOKENS", "999");
        env.Set("RIX_TIMEOUT", "15");
        env.Set("RIX_WORK_DIR", Path.GetTempPath());
        env.Set("RIX_OUTPUT_DIR", Path.GetTempPath());
        var config = Read();

        var job = Job(config);
        Assert.AreEqual("env/repo", config.Repo.ToString());
        Assert.AreEqual("env-read", config.ReadToken.Value);
        Assert.AreEqual(42, config.RunId.Value);
        Assert.AreEqual(999, job.Agent.MaxTokens.Value);
        Assert.AreEqual(15, job.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), job.WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), job.OutputDir.Value);
    }

    [TestMethod]
    public void Command_FlagsTakePrecedenceOverEnvVars()
    {
        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        var config = Read("--repo", "flag/repo", "--read-token", "r", "--run-id", "1", "--output-dir", Path.GetTempPath());

        Assert.AreEqual("flag/repo", config.Repo.ToString());
    }

    [TestMethod]
    public void Command_SelectsAgent_FromFlag()
    {
        Assert.AreEqual(AgentKind.OpenCode, Job(ReadValid("--agent", "opencode")).Agent.Kind);
    }

    [TestMethod]
    public void Command_PassesThroughModel_FromFlag()
    {
        Assert.AreEqual("openai/gpt-4o", Job(ReadValid("--model", "openai/gpt-4o")).Agent.Model);
    }

    [TestMethod]
    public void Command_PassesThroughAgentApiKeyAndEnv_FromFlags()
    {
        var agent = Job(ReadValid("--agent-api-key", "secret", "--agent-api-key-env", "ANTHROPIC_API_KEY")).Agent;

        Assert.AreEqual(new AgentCredential("ANTHROPIC_API_KEY", "secret"), agent.Credential);
    }

    [TestMethod]
    public void Command_DefaultsMaxRixCommits_WhenFlagAndEnvAbsent()
    {
        Assert.AreEqual(CiFailureConfig.DefaultMaxRixCommits, ReadValid().MaxRixCommits.Value);
    }

    [TestMethod]
    public void Command_PassesThroughMaxRixCommits_FromFlag()
    {
        Assert.AreEqual(2, ReadValid("--max-rix-commits", "2").MaxRixCommits.Value);
    }

    [TestMethod]
    [DataRow("abc", "--max-rix-commits: must be a whole number, got 'abc'")]
    [DataRow("0", "--max-rix-commits: must be between 1 and 100, got '0'")]
    [DataRow("101", "--max-rix-commits: must be between 1 and 100, got '101'")]
    public void Command_ReportsTheFlag_WhenMaxRixCommitsIsOutOfRange(string raw, string expectedError)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => ReadValid("--max-rix-commits", raw));

        Assert.AreEqual(expectedError, ex.Message);
    }

    [TestMethod]
    public void Command_DoesNotTakeAPrompt()
    {
        // The prompt describes a failure that hasn't been detected yet, so the command neither
        // requires nor accepts one - a caller passing it gets the parser's own unknown-option error.
        var parsed = CommandArgs.Parse(
            CiFailureCommand.Build(),
            "ci-failure", "--repo", "o/r", "--read-token", "r", "--run-id", "1",
            "--output-dir", Path.GetTempPath(), "--prompt", "fix it");

        Assert.AreNotEqual(0, parsed.Errors.Count);
    }

    [TestMethod]
    [DataRow("", "r", "1", "--repo is required")]
    [DataRow("noslash", "r", "1", "--repo: 'noslash' is not a valid repo identifier")]
    [DataRow("o/r", "", "1", "--read-token is required")]
    [DataRow("o/r", "r", "", "--run-id is required")]
    [DataRow("o/r", "r", "abc", "--run-id: must be a whole number, got 'abc'")]
    [DataRow("o/r", "r", "0", "--run-id: must be a positive integer, got '0'")]
    public void Command_ReportsTheFlag_WhenInputInvalid(string repo, string readToken, string runId, string expectedError)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(
            () => Read("--repo", repo, "--read-token", readToken, "--run-id", runId, "--output-dir", Path.GetTempPath()));

        StringAssert.StartsWith(ex.Message, expectedError);
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenASharedJobOptionIsMalformed()
    {
        // The shared job options are validated up front, before the run is even looked at, so a
        // typo surfaces immediately rather than only once a failure has been detected.
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => ReadValid("--max-tokens", "abc"));

        Assert.AreEqual("--max-tokens: must be a whole number, got 'abc'", ex.Message);
    }

    [TestMethod]
    public void Command_ReportsOnlyTheFirstProblem()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--repo", "", "--read-token", "", "--run-id", ""));

        Assert.AreEqual("--repo is required", ex.Message);
    }
}
