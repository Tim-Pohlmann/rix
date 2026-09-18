using Rix.CiFailure;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class CiFailureConfigTests
{
    private static CiFailureConfigResult Create
    (
        string repo = "owner/repo",
        string readToken = "read-tok",
        string runId = "123",
        string? agent = null,
        string? agentApiKey = null,
        string? agentApiKeyEnv = null
    )
    => CiFailureConfig.Create(new CiFailureInputs
    (
        RunId: runId,
        Job: new JobInputs
        (
            Repo: repo,
            ReadToken: readToken,
            WorkDir: Path.GetTempPath(),
            OutputDir: Path.GetTempPath(),
            Agent: agent,
            AgentApiKey: agentApiKey,
            AgentApiKeyEnv: agentApiKeyEnv
        )
    ));

    private static CiFailureConfig Valid(CiFailureConfigResult result) => result switch
    {
        CiFailureConfigValid v => v.Config,
        CiFailureConfigInvalid i => throw new AssertFailedException($"expected valid config, got errors: {string.Join("; ", i.Errors)}"),
        _ => throw new AssertFailedException($"unexpected result: {result}"),
    };

    private static IReadOnlyList<string> Errors(CiFailureConfigResult result) => result switch
    {
        CiFailureConfigInvalid i => i.Errors,
        _ => throw new AssertFailedException("expected an invalid config"),
    };

    [TestMethod]
    public void Create_ReturnsValid_ForValidInputs()
    {
        var config = Valid(Create());
        Assert.AreEqual("owner/repo", config.Repo.ToString());
        Assert.AreEqual(123, config.RunId.Value);
        Assert.AreEqual("read-tok", config.ReadToken.Value);
    }

    [TestMethod]
    public void Create_RejectsEmptyRepo_Once()
    {
        // --repo is a job input that CiFailureConfig also needs for itself; it must still be
        // validated in exactly one place, not complained about once per consumer.
        var errors = Errors(Create(repo: ""));
        Assert.AreEqual(1, errors.Count(e => e.Contains("--repo is required")));
    }

    [TestMethod]
    public void Create_RejectsEmptyReadToken_Once()
    {
        var errors = Errors(Create(readToken: ""));
        Assert.AreEqual(1, errors.Count(e => e.Contains("--read-token is required")));
    }

    [TestMethod]
    public void Create_RejectsEmptyRunId()
    {
        Assert.IsTrue(Errors(Create(runId: "")).Any(e => e.Contains("--run-id is required")));
    }

    [TestMethod]
    public void Create_RejectsMalformedAgent()
    {
        Assert.IsTrue(Errors(Create(agent: "not-a-real-agent")).Any(e => e.Contains("--agent")));
    }

    [TestMethod]
    public void Create_DoesNotRequireAPrompt()
    {
        // The prompt describes a failure that hasn't been detected yet, so validation must pass
        // without one - and ToJobConfig supplies it later.
        Assert.IsFalse(Errors(Create(runId: "")).Any(e => e.Contains("--prompt")));
    }

    [TestMethod]
    public void Create_CollectsErrors_FromBothCiFailureAndJobSides()
    {
        var errors = Errors(Create(runId: "", agent: "not-a-real-agent"));
        Assert.IsTrue(errors.Any(e => e.Contains("--run-id is required")));
        Assert.IsTrue(errors.Any(e => e.Contains("--agent")));
    }

    [TestMethod]
    public void Create_ThreadsAgentApiKeyAndEnv_ThroughToJobConfig()
    {
        var job = Valid(Create(agentApiKey: "secret", agentApiKeyEnv: "ANTHROPIC_API_KEY"))
            .ToJobConfig("fix it", new BranchName("rix/fix"));
        Assert.AreEqual("secret", job.Agent.ApiKey);
        Assert.AreEqual("ANTHROPIC_API_KEY", job.Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void ToJobConfig_AppliesPromptAndAllowedPushBranch()
    {
        var job = Valid(Create()).ToJobConfig("fix it", new BranchName("rix/fix"));
        Assert.AreEqual("fix it", job.Agent.Prompt);
        Assert.AreEqual("rix/fix", job.AllowedPushBranches.Single().Value);
        Assert.AreEqual("owner/repo", job.Repo.ToString());
        Assert.AreEqual("read-tok", job.ReadToken.Value);
    }

    /// <summary>Commas are legal in git branch names, and the allow-list is what stops <c>/push</c>
    /// delivering anywhere else — so the failing branch must survive as one entry, rather than being
    /// split into halves that permit two other branches and reject this one.</summary>
    [TestMethod]
    public void ToJobConfig_KeepsBranchWhole_WhenBranchNameContainsAComma()
    {
        var job = Valid(Create()).ToJobConfig("fix it", new BranchName("feature/a,b"));
        Assert.AreEqual("feature/a,b", job.AllowedPushBranches.Single().Value);
    }

    [TestMethod]
    public void Create_RejectsApiKeyEnv_ThatIsNotCredentialShaped()
    {
        Assert.IsTrue(Errors(Create(agentApiKey: "secret", agentApiKeyEnv: "NOT_CREDENTIAL_SHAPED")).Any(e => e.Contains("--agent-api-key-env")));
    }
}
