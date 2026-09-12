using Rix.CiFailure;

namespace Rix.Tests;

[TestClass]
public class CiFailureConfigTests
{
    private static CiFailureConfigResult Create
    (
        string repo = "owner/repo",
        string readToken = "read-tok",
        string runId = "123"
    )
    => CiFailureConfig.Create(repo, readToken, runId);

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
        Assert.AreEqual("read-tok", config.ReadToken.Value);
        Assert.AreEqual(123, config.RunId);
    }

    [TestMethod]
    public void Create_RejectsEmptyRepo()
    {
        Assert.IsTrue(Errors(Create(repo: "")).Any(e => e.Contains("--repo is required")));
    }

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    public void Create_RejectsMalformedRepo(string repo)
    {
        Assert.IsTrue(Errors(Create(repo: repo)).Any(e => e.Contains("repo identifier")));
    }

    [TestMethod]
    public void Create_RejectsEmptyReadToken()
    {
        Assert.IsTrue(Errors(Create(readToken: "")).Any(e => e.Contains("--read-token")));
    }

    [TestMethod]
    public void Create_RejectsEmptyRunId()
    {
        Assert.IsTrue(Errors(Create(runId: "")).Any(e => e.Contains("--run-id is required")));
    }

    [TestMethod]
    [DataRow("abc")]
    [DataRow("0")]
    [DataRow("-5")]
    public void Create_RejectsMalformedRunId(string runId)
    {
        Assert.IsTrue(Errors(Create(runId: runId)).Any(e => e.Contains("--run-id must be a positive integer")));
    }

    [TestMethod]
    public void Create_CollectsAllErrors()
    {
        var errors = Errors(Create(repo: "", readToken: "", runId: ""));
        Assert.IsTrue(errors.Count >= 3, $"expected several errors, got: {string.Join("; ", errors)}");
    }
}
