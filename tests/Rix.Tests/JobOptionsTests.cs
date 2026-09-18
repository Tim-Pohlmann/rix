using Rix.Cli;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

/// <summary>Covers <see cref="JobOptions.ReadSettings"/>, the boundary that turns the flags shared
/// by <c>job</c> and <c>ci-failure</c> into a <see cref="JobSettings"/>: defaults, parsing, and the
/// message each malformed flag produces. The two flags only <c>job</c> owns (<c>--prompt</c>,
/// <c>--allowed-push-branches</c>) are covered by <see cref="JobCommandTests"/>.</summary>
[TestClass]
public class JobOptionsTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static JobSettings Read(params string[] args) => ReadWith("owner/repo", "read-tok", ExistingDir, args);

    private static JobSettings ReadWithOutputDir(string outputDir, params string[] args) => ReadWith("owner/repo", "read-tok", outputDir, args);

    private static JobSettings ReadWith(string repo, string readToken, string outputDir, params string[] args)
    {
        var root = new RootCommand();
        root.AddCommand(JobCommand.Build(_ => Task.FromResult(0)));
        var parsed = root.Parse(["job", "--repo", repo, "--read-token", readToken, "--output-dir", outputDir, .. args]);
        return JobOptions.ReadSettings(parsed);
    }

    private static string ErrorOf(Func<JobSettings> read) => Assert.ThrowsExactly<InvalidInputException>(() => read()).Message;

    [TestMethod]
    public void ReadSettings_AppliesDefaults()
    {
        var settings = Read();

        Assert.AreEqual(JobConfig.DefaultMaxTokens, settings.MaxTokens.Value);
        Assert.AreEqual(JobConfig.DefaultTimeoutMinutes, settings.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), settings.WorkDir.Value);
        Assert.AreEqual(JobConfig.DefaultAgent, settings.Agent);
        Assert.IsNull(settings.Model);
        Assert.IsNull(settings.ApiKey);
        Assert.IsNull(settings.ApiKeyEnv);
    }

    [TestMethod]
    public void ReadSettings_ReadsRepoAndReadToken()
    {
        var settings = Read();

        Assert.AreEqual("owner/repo", settings.Repo.ToString());
        Assert.AreEqual("read-tok", settings.ReadToken.Value);
    }

    [TestMethod]
    public void ReadSettings_RejectsEmptyRepo()
    => Assert.AreEqual("--repo is required", ErrorOf(() => ReadWith("", "read-tok", ExistingDir)));

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void ReadSettings_RejectsMalformedRepo(string repo)
    {
        var error = ErrorOf(() => ReadWith(repo, "read-tok", ExistingDir));
        StringAssert.StartsWith(error, "--repo: ");
        StringAssert.Contains(error, "repo identifier");
    }

    [TestMethod]
    public void ReadSettings_RejectsEmptyReadToken()
    => Assert.AreEqual("--read-token is required", ErrorOf(() => ReadWith("owner/repo", "", ExistingDir)));

    [TestMethod]
    public void ReadSettings_ReportsTheFirstProblemOnly()
    => Assert.AreEqual("--repo is required", ErrorOf(() => ReadWith("", "", "")));

    [TestMethod]
    public void ToJob_CompletesSettings_WithPromptAndAllowedPushBranches()
    {
        var job = Read("--agent", "claude", "--max-tokens", "1234").ToJob("Fix the bug", [new BranchName("main")]);

        Assert.AreEqual("owner/repo", job.Repo.ToString());
        Assert.AreEqual("read-tok", job.ReadToken.Value);
        Assert.AreEqual("Fix the bug", job.Agent.Prompt);
        Assert.AreEqual(Rix.Agents.AgentKind.Claude, job.Agent.Kind);
        Assert.AreEqual(1234, job.Agent.MaxTokens.Value);
        Assert.AreEqual("main", job.AllowedPushBranches.Single().Value);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void ToJob_RejectsBlankPrompt(string prompt)
    {
        // Not an input error: every caller either required the prompt from the user already or built
        // it from a template, so a blank one here is a bug in that caller.
        Assert.ThrowsExactly<ArgumentException>(() => Read().ToJob(prompt, []));
    }

    [TestMethod]
    public void ReadSettings_OverridesDefaults()
    {
        var config = Read("--max-tokens", "1000", "--timeout", "5", "--work-dir", ExistingDir);

        Assert.AreEqual(1000, config.MaxTokens.Value);
        Assert.AreEqual(5, config.TimeoutMinutes.Value);
    }

    [TestMethod]
    public void ReadSettings_RejectsNonPositiveMaxTokens()
    => Assert.AreEqual("--max-tokens: must be a positive integer, got '0'", ErrorOf(() => Read("--max-tokens", "0")));

    [TestMethod]
    public void ReadSettings_RejectsNonPositiveTimeout()
    => Assert.AreEqual("--timeout: must be a positive integer, got '-1'", ErrorOf(() => Read("--timeout", "-1")));

    [TestMethod]
    public void ReadSettings_RejectsNonNumericMaxTokens()
    => Assert.AreEqual("--max-tokens: must be a positive integer, got 'abc'", ErrorOf(() => Read("--max-tokens", "abc")));

    [TestMethod]
    public void ReadSettings_RejectsNonNumericTimeout()
    => Assert.AreEqual("--timeout: must be a positive integer, got 'abc'", ErrorOf(() => Read("--timeout", "abc")));

    [TestMethod]
    public void ReadSettings_RejectsNonExistentWorkDir()
    => Assert.AreEqual("--work-dir: directory does not exist: /nonexistent/path/xyz", ErrorOf(() => Read("--work-dir", "/nonexistent/path/xyz")));

    [TestMethod]
    public void ReadSettings_RejectsEmptyOutputDir()
    => Assert.AreEqual("--output-dir is required", ErrorOf(() => ReadWithOutputDir("")));

    [TestMethod]
    public void ReadSettings_RejectsNonExistentOutputDir()
    => Assert.AreEqual("--output-dir: directory does not exist: /nonexistent/out", ErrorOf(() => ReadWithOutputDir("/nonexistent/out")));

    [TestMethod]
    public void ReadSettings_DefaultsWorkDirToTemp_WhenBlank()
    {
        Assert.AreEqual(Path.GetTempPath(), Read("--work-dir", "").WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), Read("--work-dir", "   ").WorkDir.Value);
    }

    [TestMethod]
    public void ReadSettings_SelectsAgent()
    {
        Assert.AreEqual(Rix.Agents.AgentKind.Claude, Read("--agent", "claude").Agent);
        Assert.AreEqual(Rix.Agents.AgentKind.Pi, Read("--agent", "pi").Agent);
    }

    [TestMethod]
    public void ReadSettings_RejectsUnknownAgent()
    {
        var error = ErrorOf(() => Read("--agent", "devin"));
        StringAssert.StartsWith(error, "--agent: ");
        StringAssert.Contains(error, "devin");
    }

    [TestMethod]
    public void ReadSettings_DefaultsModelToNull_WhenBlank()
    {
        // Unset means "let the agent CLI pick its own default" for every agent — opencode and
        // claude both fall back to a free/default model on their own when --model is omitted.
        Assert.IsNull(Read("--agent", "opencode", "--model", "").Model);
        Assert.IsNull(Read("--agent", "claude").Model);
        Assert.IsNull(Read("--agent", "pi").Model);
    }

    [TestMethod]
    public void ReadSettings_PassesThroughExplicitModel()
    {
        Assert.AreEqual("openai/gpt-4o", Read("--agent", "opencode", "--model", "openai/gpt-4o").Model);
        Assert.AreEqual("claude-opus-4", Read("--agent", "claude", "--model", "claude-opus-4").Model);
        Assert.AreEqual("openai/gpt-4o", Read("--agent", "pi", "--model", "openai/gpt-4o").Model);
    }

    [TestMethod]
    public void ReadSettings_LeavesApiKeyAndEnvNull_WhenNoKeySupplied()
    {
        var config = Read("--agent-api-key-env", "ANTHROPIC_API_KEY");

        Assert.IsNull(config.ApiKey);
        Assert.IsNull(config.ApiKeyEnv);
    }

    [TestMethod]
    public void ReadSettings_DefaultsApiKeyEnv_PerAgent_WhenKeySuppliedWithoutOverride()
    {
        Assert.AreEqual("OPENCODE_API_KEY", Read("--agent", "opencode", "--agent-api-key", "secret").ApiKeyEnv);
        Assert.AreEqual("ANTHROPIC_API_KEY", Read("--agent", "claude", "--agent-api-key", "secret").ApiKeyEnv);
    }

    [TestMethod]
    public void ReadSettings_UsesExplicitApiKeyEnv_WhenValid()
    {
        var config = Read("--agent", "opencode", "--agent-api-key", "secret", "--agent-api-key-env", "OPENAI_API_KEY");

        Assert.AreEqual("secret", config.ApiKey);
        Assert.AreEqual("OPENAI_API_KEY", config.ApiKeyEnv);
    }

    [TestMethod]
    public void ReadSettings_RejectsPiAgent_WithApiKey_AndNoEnvOverride()
    {
        var error = ErrorOf(() => Read("--agent", "pi", "--agent-api-key", "secret"));
        StringAssert.StartsWith(error, "--agent-api-key-env: ");
        StringAssert.Contains(error, "pi");
    }

    [TestMethod]
    [DataRow("NOT_A_CREDENTIAL")]
    // Full rejection matrix (RIX_*, AGENT_API_KEY*, GITHUB_*) is covered by
    // AgentCredentialTests; this just proves ReadSettings wires the error through.
    [DataRow("RIX_AGENT")]
    public void ReadSettings_RejectsApiKeyEnv_ThatIsNotCredentialShaped(string envName)
    {
        var error = ErrorOf(() => Read("--agent-api-key", "secret", "--agent-api-key-env", envName));
        StringAssert.StartsWith(error, "--agent-api-key-env: ");
        StringAssert.Contains(error, envName);
    }
}
