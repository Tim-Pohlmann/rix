using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobConfigTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static JobConfigResult Create(
        string repo = "owner/repo",
        string prompt = "Fix the bug",
        string readToken = "read-tok",
        string? maxTokens = null,
        string? timeoutMinutes = null,
        string? workDir = null,
        string? outputDir = null,
        string? agent = null,
        string? model = null,
        string? agentApiKey = null,
        string? agentApiKeyEnv = null,
        string? allowedPushBranches = null)
    => JobConfig.Create(new JobInputs
    (
        Repo: repo,
        Prompt: prompt,
        ReadToken: readToken,
        MaxTokens: maxTokens,
        TimeoutMinutes: timeoutMinutes,
        WorkDir: workDir,
        OutputDir: outputDir ?? ExistingDir,
        Agent: agent,
        Model: model,
        AgentApiKey: agentApiKey,
        AgentApiKeyEnv: agentApiKeyEnv,
        AllowedPushBranches: allowedPushBranches
    ));

    private static JobConfig Valid(JobConfigResult result) => result switch
    {
        JobConfigValid v => v.Config,
        JobConfigInvalid i => throw new AssertFailedException($"expected valid config, got errors: {string.Join("; ", i.Errors)}"),
        _ => throw new AssertFailedException($"unexpected result: {result}"),
    };

    private static IReadOnlyList<string> Errors(JobConfigResult result) => result switch
    {
        JobConfigInvalid i => i.Errors,
        _ => throw new AssertFailedException("expected an invalid config"),
    };

    [TestMethod]
    public void Create_AppliesDefaults()
    {
        var config = Valid(Create());

        Assert.AreEqual(JobConfig.DefaultMaxTokens, config.Agent.MaxTokens.Value);
        Assert.AreEqual(JobConfig.DefaultTimeoutMinutes, config.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), config.WorkDir.Value);
    }

    [TestMethod]
    public void Create_OverridesDefaults()
    {
        var config = Valid(Create(maxTokens: "1000", timeoutMinutes: "5", workDir: Path.GetTempPath()));

        Assert.AreEqual(1000, config.Agent.MaxTokens.Value);
        Assert.AreEqual(5, config.TimeoutMinutes.Value);
    }

    [TestMethod]
    public void Create_ParsesRepoIntoStrongType()
    {
        var config = Valid(Create(repo: "owner/repo"));
        Assert.AreEqual("owner/repo", config.Repo.ToString());
    }

    [TestMethod]
    public void Create_ReturnsValid_ForValidInputs()
    {
        Assert.IsInstanceOfType<JobConfigValid>(Create());
    }

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void RepoIdentifier_Parse_RejectsInvalidFormat(string repo)
    {
        Assert.IsInstanceOfType<ParseError<RepoIdentifier>>(RepoIdentifier.Parse(repo));
    }

    [TestMethod]
    public void RepoIdentifier_Parse_AcceptsOwnerSlashRepo()
    {
        Assert.IsInstanceOfType<ParseSuccess<RepoIdentifier>>(RepoIdentifier.Parse("owner/repo"));
    }

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void Create_RejectsMalformedRepo(string repo)
    {
        var errors = Errors(Create(repo: repo));
        Assert.IsTrue(errors.Any(e => e.Contains("repo identifier")), $"expected a repo-format error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void Create_RejectsEmptyRepo()
    {
        Assert.IsTrue(Errors(Create(repo: "")).Any(e => e.Contains("--repo is required")));
    }

    [TestMethod]
    public void Create_RejectsEmptyPrompt()
    {
        Assert.IsTrue(Errors(Create(prompt: "")).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsEmptyReadToken()
    {
        Assert.IsTrue(Errors(Create(readToken: "")).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsNonPositiveMaxTokens()
    {
        Assert.IsTrue(Errors(Create(maxTokens: "0")).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsNonPositiveTimeout()
    {
        Assert.IsTrue(Errors(Create(timeoutMinutes: "-1")).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsNonNumericMaxTokens()
    {
        var errors = Errors(Create(maxTokens: "abc"));
        Assert.IsTrue(errors.Any(e => e.Contains("--max-tokens") && e.Contains("integer")), $"expected an integer-format error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void Create_RejectsNonNumericTimeout()
    {
        var errors = Errors(Create(timeoutMinutes: "abc"));
        Assert.IsTrue(errors.Any(e => e.Contains("--timeout") && e.Contains("integer")), $"expected an integer-format error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void Create_RejectsNonExistentWorkDir()
    {
        Assert.IsTrue(Errors(Create(workDir: "/nonexistent/path/xyz")).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsEmptyOutputDir()
    {
        Assert.IsTrue(Errors(Create(outputDir: "")).Any(e => e.Contains("--output-dir")));
    }

    [TestMethod]
    public void Create_RejectsNonExistentOutputDir()
    {
        Assert.IsTrue(Errors(Create(outputDir: "/nonexistent/out")).Any(e => e.Contains("--output-dir")));
    }

    [TestMethod]
    public void Create_CollectsAllErrors()
    {
        var errors = Errors(Create(repo: "", prompt: "", readToken: ""));
        Assert.IsTrue(errors.Count >= 3, $"expected several errors, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void DirectoryPath_Parse_NormalisesRelativeToAbsolute()
    {
        var result = DirectoryPath.Parse(".");
        var parsed = result switch
        {
            ParseSuccess<DirectoryPath> s => s.Value,
            _ => throw new AssertFailedException($"expected a valid path, got: {result}"),
        };
        Assert.IsTrue(Path.IsPathRooted(parsed.Value), $"expected an absolute path, got: {parsed.Value}");
        Assert.AreEqual(Path.GetFullPath("."), parsed.Value);
    }

    [TestMethod]
    public void DirectoryPath_Parse_RejectsNonExistent()
    {
        Assert.IsInstanceOfType<ParseError<DirectoryPath>>(DirectoryPath.Parse("/nonexistent/path/xyz"));
    }

    [TestMethod]
    public void Create_DefaultsWorkDirToTemp_WhenNullOrWhitespace()
    {
        Assert.AreEqual(Path.GetTempPath(), Valid(Create(workDir: null)).WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), Valid(Create(workDir: "")).WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), Valid(Create(workDir: "   ")).WorkDir.Value);
    }

    [TestMethod]
    public void Create_DefaultsAgentToOpenCode_AndSelectsClaude()
    {
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, Valid(Create()).Agent.Kind);
        Assert.AreEqual(Rix.Agents.AgentKind.Claude, Valid(Create(agent: "claude")).Agent.Kind);
        Assert.AreEqual(Rix.Agents.AgentKind.Pi, Valid(Create(agent: "pi")).Agent.Kind);
    }

    [TestMethod]
    public void Create_RejectsUnknownAgent()
    {
        Assert.IsTrue(Errors(Create(agent: "devin")).Any(e => e.Contains("--agent")));
    }

    [TestMethod]
    public void Create_DefaultsModelToNull_WhenAbsentOrBlank()
    {
        // Unset means "let the agent CLI pick its own default" for every agent — opencode and
        // claude both fall back to a free/default model on their own when --model is omitted.
        Assert.IsNull(Valid(Create(agent: "opencode")).Agent.Model);
        Assert.IsNull(Valid(Create(agent: "opencode", model: "")).Agent.Model);
        Assert.IsNull(Valid(Create(agent: "claude")).Agent.Model);
        Assert.IsNull(Valid(Create(agent: "pi")).Agent.Model);
    }

    [TestMethod]
    public void Create_PassesThroughExplicitModel()
    {
        Assert.AreEqual("openai/gpt-4o", Valid(Create(agent: "opencode", model: "openai/gpt-4o")).Agent.Model);
        Assert.AreEqual("claude-opus-4", Valid(Create(agent: "claude", model: "claude-opus-4")).Agent.Model);
        Assert.AreEqual("openai/gpt-4o", Valid(Create(agent: "pi", model: "openai/gpt-4o")).Agent.Model);
    }

    [TestMethod]
    public void Create_DefaultsAllowedPushBranches_ToEmptyList()
    {
        Assert.AreEqual(0, Valid(Create()).AllowedPushBranches.Count);
    }

    [TestMethod]
    public void Create_ParsesAllowedPushBranches_FromCommaSeparatedList()
    {
        var config = Valid(Create(allowedPushBranches: "rix/continue-a, rix/continue-b"));

        Assert.AreEqual(2, config.AllowedPushBranches.Count);
        Assert.AreEqual("rix/continue-a", config.AllowedPushBranches[0].Value);
        Assert.AreEqual("rix/continue-b", config.AllowedPushBranches[1].Value);
    }

    [TestMethod]
    public void Create_DropsBlankAndDuplicateAllowedPushBranches()
    {
        var config = Valid(Create(allowedPushBranches: "rix/a,,rix/a, rix/b"));

        Assert.AreEqual(2, config.AllowedPushBranches.Count);
        CollectionAssert.AreEqual(
            new[] { "rix/a", "rix/b" },
            config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public void Create_RejectsMalformedAllowedPushBranches()
    {
        var errors = Errors(Create(allowedPushBranches: "rix/good,main,prod"));

        Assert.IsTrue(errors.Any(e => e.Contains("--allowed-push-branches")), $"expected an allowed-push-branches error, got: {string.Join("; ", errors)}");
        Assert.IsTrue(errors.Any(e => e.Contains("rix/*")), $"expected a branch-format error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void Create_RejectsAllowedPushBranches_ThatAreNotRixBranches()
    {
        var errors = Errors(Create(allowedPushBranches: "main"));

        Assert.IsTrue(errors.Any(e => e.Contains("--allowed-push-branches")), $"expected an allowed-push-branches error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void Create_LeavesApiKeyAndEnvNull_WhenNoKeySupplied()
    {
        var config = Valid(Create(agentApiKeyEnv: "ANTHROPIC_API_KEY"));

        Assert.IsNull(config.Agent.ApiKey);
        Assert.IsNull(config.Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void Create_DefaultsApiKeyEnv_PerAgent_WhenKeySuppliedWithoutOverride()
    {
        Assert.AreEqual("OPENCODE_API_KEY", Valid(Create(agent: "opencode", agentApiKey: "secret")).Agent.ApiKeyEnv);
        Assert.AreEqual("ANTHROPIC_API_KEY", Valid(Create(agent: "claude", agentApiKey: "secret")).Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void Create_UsesExplicitApiKeyEnv_WhenValid()
    {
        var config = Valid(Create(agent: "opencode", agentApiKey: "secret", agentApiKeyEnv: "OPENAI_API_KEY"));

        Assert.AreEqual("secret", config.Agent.ApiKey);
        Assert.AreEqual("OPENAI_API_KEY", config.Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void Create_RejectsPiAgent_WithApiKey_AndNoEnvOverride()
    {
        var errors = Errors(Create(agent: "pi", agentApiKey: "secret"));

        Assert.IsTrue(errors.Any(e => e.Contains("--agent-api-key-env") && e.Contains("pi")), $"expected a pi-requires-env error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void Create_RejectsApiKeyEnv_ThatIsNotCredentialShaped()
    {
        var errors = Errors(Create(agentApiKey: "secret", agentApiKeyEnv: "NOT_A_CREDENTIAL"));

        Assert.IsTrue(errors.Any(e => e.Contains("--agent-api-key-env")), $"expected an agent-api-key-env error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    [DataRow("RIX_AGENT")]
    [DataRow("AGENT_API_KEY_EXTRA")]
    [DataRow("GITHUB_TOKEN")]
    public void Create_RejectsApiKeyEnv_ThatNamesARixOrGitHubRuntimeVariable(string envName)
    {
        var errors = Errors(Create(agentApiKey: "secret", agentApiKeyEnv: envName));

        Assert.IsTrue(errors.Any(e => e.Contains("--agent-api-key-env")), $"expected an agent-api-key-env error, got: {string.Join("; ", errors)}");
    }
}
