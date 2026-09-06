using Rix.CiFailure;

namespace Rix.Tests;

[TestClass]
public class CiFailureJobConfigTests
{
    private static CiFailureJobConfigResult Create
    (
        string repo = "owner/repo",
        string readToken = "read-tok",
        string runId = "123",
        string? agent = null,
        string? agentApiKey = null,
        string? agentApiKeyEnv = null
    )
    => CiFailureJobConfig.Create(new CiFailureJobInputs
    (
        Repo: repo,
        ReadToken: readToken,
        RunId: runId,
        WorkDir: Path.GetTempPath(),
        OutputDir: Path.GetTempPath(),
        Agent: agent,
        AgentApiKey: agentApiKey,
        AgentApiKeyEnv: agentApiKeyEnv
    ));

    private static CiFailureJobConfig Valid(CiFailureJobConfigResult result) => result switch
    {
        CiFailureJobConfigValid v => v.Config,
        CiFailureJobConfigInvalid i => throw new AssertFailedException($"expected valid config, got errors: {string.Join("; ", i.Errors)}"),
        _ => throw new AssertFailedException($"unexpected result: {result}"),
    };

    private static IReadOnlyList<string> Errors(CiFailureJobConfigResult result) => result switch
    {
        CiFailureJobConfigInvalid i => i.Errors,
        _ => throw new AssertFailedException("expected an invalid config"),
    };

    [TestMethod]
    public void Create_ReturnsValid_ForValidInputs()
    {
        var config = Valid(Create());
        Assert.AreEqual("owner/repo", config.CiFailure.Repo.ToString());
        Assert.AreEqual(123, config.CiFailure.RunId);
        Assert.AreEqual("owner/repo", config.Job.Repo.ToString());
        Assert.AreEqual("read-tok", config.Job.ReadToken.Value);
    }

    [TestMethod]
    public void Create_RejectsEmptyRepo_Once()
    {
        // Repo is validated on both the CiFailure and Job side; Create must not surface the same
        // complaint twice.
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
    public void Create_CollectsErrors_FromBothCiFailureAndJobSides()
    {
        var errors = Errors(Create(runId: "", agent: "not-a-real-agent"));
        Assert.IsTrue(errors.Any(e => e.Contains("--run-id is required")));
        Assert.IsTrue(errors.Any(e => e.Contains("--agent")));
    }

    [TestMethod]
    public void Create_ThreadsAgentApiKeyAndEnv_ThroughToJobConfig()
    {
        var config = Valid(Create(agentApiKey: "secret", agentApiKeyEnv: "ANTHROPIC_API_KEY"));
        Assert.AreEqual("secret", config.Job.Agent.ApiKey);
        Assert.AreEqual("ANTHROPIC_API_KEY", config.Job.Agent.ApiKeyEnv);
    }

    [TestMethod]
    public void Create_RejectsApiKeyEnv_ThatIsNotCredentialShaped()
    {
        Assert.IsTrue(Errors(Create(agentApiKey: "secret", agentApiKeyEnv: "NOT_CREDENTIAL_SHAPED")).Any(e => e.Contains("--agent-api-key-env")));
    }
}
