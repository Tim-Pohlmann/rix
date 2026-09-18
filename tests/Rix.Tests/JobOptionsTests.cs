using Rix.Cli;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

/// <summary>Covers <see cref="JobOptions.ReadConfig"/>, the boundary that turns the shared
/// <c>job</c>/<c>ci-failure-job</c> flags into a <see cref="JobConfig"/>: defaults, parsing, and
/// the message each malformed flag produces. The flags only <c>job</c> owns (<c>--repo</c>,
/// <c>--prompt</c>, <c>--read-token</c>, <c>--allowed-push-branches</c>) are covered by
/// <see cref="JobCommandTests"/>.</summary>
[TestClass]
public class JobOptionsTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static JobConfig Read(params string[] args) => ReadWithOutputDir(ExistingDir, args);

    private static JobConfig ReadWithOutputDir(string outputDir, params string[] args)
    {
        var root = new RootCommand();
        root.AddCommand(JobCommand.Build(_ => Task.FromResult(0)));
        var parsed = root.Parse(["job", "--output-dir", outputDir, .. args]);
        return JobOptions.ReadConfig(parsed, new RepoIdentifier("owner/repo"), new GitReadToken("read-tok"), "Fix the bug", []);
    }

    private static string ErrorOf(Func<JobConfig> read) => Assert.ThrowsExactly<InvalidInputException>(() => read()).Message;

    [TestMethod]
    public void ReadConfig_AppliesDefaults()
    {
        var config = Read();

        Assert.AreEqual(JobConfig.DefaultMaxTokens, config.Agent.MaxTokens.Value);
        Assert.AreEqual(JobConfig.DefaultTimeoutMinutes, config.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), config.WorkDir.Value);
        Assert.AreEqual(JobConfig.DefaultAgent, config.Agent.Kind);
        Assert.IsNull(config.Agent.Model);
        Assert.IsNull(config.Agent.ApiKey);
        Assert.IsNull(config.Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void ReadConfig_PassesThroughTheValuesItIsGiven()
    {
        var config = Read();

        Assert.AreEqual("owner/repo", config.Repo.ToString());
        Assert.AreEqual("read-tok", config.ReadToken.Value);
        Assert.AreEqual("Fix the bug", config.Agent.Prompt);
        Assert.AreEqual(0, config.AllowedPushBranches.Count);
    }

    [TestMethod]
    public void ReadConfig_OverridesDefaults()
    {
        var config = Read("--max-tokens", "1000", "--timeout", "5", "--work-dir", ExistingDir);

        Assert.AreEqual(1000, config.Agent.MaxTokens.Value);
        Assert.AreEqual(5, config.TimeoutMinutes.Value);
    }

    [TestMethod]
    public void ReadConfig_RejectsNonPositiveMaxTokens()
    => Assert.AreEqual("--max-tokens: must be a positive integer, got '0'", ErrorOf(() => Read("--max-tokens", "0")));

    [TestMethod]
    public void ReadConfig_RejectsNonPositiveTimeout()
    => Assert.AreEqual("--timeout: must be a positive integer, got '-1'", ErrorOf(() => Read("--timeout", "-1")));

    [TestMethod]
    public void ReadConfig_RejectsNonNumericMaxTokens()
    => Assert.AreEqual("--max-tokens: must be a positive integer, got 'abc'", ErrorOf(() => Read("--max-tokens", "abc")));

    [TestMethod]
    public void ReadConfig_RejectsNonNumericTimeout()
    => Assert.AreEqual("--timeout: must be a positive integer, got 'abc'", ErrorOf(() => Read("--timeout", "abc")));

    [TestMethod]
    public void ReadConfig_RejectsNonExistentWorkDir()
    => Assert.AreEqual("--work-dir: directory does not exist: /nonexistent/path/xyz", ErrorOf(() => Read("--work-dir", "/nonexistent/path/xyz")));

    [TestMethod]
    public void ReadConfig_RejectsEmptyOutputDir()
    => Assert.AreEqual("--output-dir is required", ErrorOf(() => ReadWithOutputDir("")));

    [TestMethod]
    public void ReadConfig_RejectsNonExistentOutputDir()
    => Assert.AreEqual("--output-dir: directory does not exist: /nonexistent/out", ErrorOf(() => ReadWithOutputDir("/nonexistent/out")));

    [TestMethod]
    public void ReadConfig_DefaultsWorkDirToTemp_WhenBlank()
    {
        Assert.AreEqual(Path.GetTempPath(), Read("--work-dir", "").WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), Read("--work-dir", "   ").WorkDir.Value);
    }

    [TestMethod]
    public void ReadConfig_SelectsAgent()
    {
        Assert.AreEqual(Rix.Agents.AgentKind.Claude, Read("--agent", "claude").Agent.Kind);
        Assert.AreEqual(Rix.Agents.AgentKind.Pi, Read("--agent", "pi").Agent.Kind);
    }

    [TestMethod]
    public void ReadConfig_RejectsUnknownAgent()
    {
        var error = ErrorOf(() => Read("--agent", "devin"));
        StringAssert.StartsWith(error, "--agent: ");
        StringAssert.Contains(error, "devin");
    }

    [TestMethod]
    public void ReadConfig_DefaultsModelToNull_WhenBlank()
    {
        // Unset means "let the agent CLI pick its own default" for every agent — opencode and
        // claude both fall back to a free/default model on their own when --model is omitted.
        Assert.IsNull(Read("--agent", "opencode", "--model", "").Agent.Model);
        Assert.IsNull(Read("--agent", "claude").Agent.Model);
        Assert.IsNull(Read("--agent", "pi").Agent.Model);
    }

    [TestMethod]
    public void ReadConfig_PassesThroughExplicitModel()
    {
        Assert.AreEqual("openai/gpt-4o", Read("--agent", "opencode", "--model", "openai/gpt-4o").Agent.Model);
        Assert.AreEqual("claude-opus-4", Read("--agent", "claude", "--model", "claude-opus-4").Agent.Model);
        Assert.AreEqual("openai/gpt-4o", Read("--agent", "pi", "--model", "openai/gpt-4o").Agent.Model);
    }

    [TestMethod]
    public void ReadConfig_LeavesApiKeyAndEnvNull_WhenNoKeySupplied()
    {
        var config = Read("--agent-api-key-env", "ANTHROPIC_API_KEY");

        Assert.IsNull(config.Agent.ApiKey);
        Assert.IsNull(config.Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void ReadConfig_DefaultsApiKeyEnv_PerAgent_WhenKeySuppliedWithoutOverride()
    {
        Assert.AreEqual("OPENCODE_API_KEY", Read("--agent", "opencode", "--agent-api-key", "secret").Agent.ApiKeyEnv);
        Assert.AreEqual("ANTHROPIC_API_KEY", Read("--agent", "claude", "--agent-api-key", "secret").Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void ReadConfig_UsesExplicitApiKeyEnv_WhenValid()
    {
        var config = Read("--agent", "opencode", "--agent-api-key", "secret", "--agent-api-key-env", "OPENAI_API_KEY");

        Assert.AreEqual("secret", config.Agent.ApiKey);
        Assert.AreEqual("OPENAI_API_KEY", config.Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void ReadConfig_RejectsPiAgent_WithApiKey_AndNoEnvOverride()
    {
        var error = ErrorOf(() => Read("--agent", "pi", "--agent-api-key", "secret"));
        StringAssert.StartsWith(error, "--agent-api-key-env: ");
        StringAssert.Contains(error, "pi");
    }

    [TestMethod]
    [DataRow("NOT_A_CREDENTIAL")]
    // Full rejection matrix (RIX_*, AGENT_API_KEY*, GITHUB_*) is covered by
    // AgentCredentialTests; this just proves ReadConfig wires the error through.
    [DataRow("RIX_AGENT")]
    public void ReadConfig_RejectsApiKeyEnv_ThatIsNotCredentialShaped(string envName)
    {
        var error = ErrorOf(() => Read("--agent-api-key", "secret", "--agent-api-key-env", envName));
        StringAssert.StartsWith(error, "--agent-api-key-env: ");
        StringAssert.Contains(error, envName);
    }
}
