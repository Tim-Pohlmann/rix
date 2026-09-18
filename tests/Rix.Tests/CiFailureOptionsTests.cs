using Rix.CiFailure;
using Rix.Cli;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

/// <summary>Covers <see cref="CiFailureOptions.ReadConfig"/>, the boundary that turns the shared
/// <c>ci-failure</c>/<c>ci-failure-job</c> flags into a <see cref="CiFailureConfig"/>.</summary>
[TestClass]
public class CiFailureOptionsTests
{
    private static CiFailureConfig Read(string repo = "owner/repo", string readToken = "read-tok", string runId = "123")
    {
        var root = new RootCommand();
        root.AddCommand(CiFailureCommand.Build(_ => Task.FromResult(0)));
        var parsed = root.Parse(["ci-failure", "--repo", repo, "--read-token", readToken, "--run-id", runId]);
        return CiFailureOptions.ReadConfig(parsed);
    }

    private static string ErrorOf(Func<CiFailureConfig> read) => Assert.ThrowsExactly<InvalidInputException>(() => read()).Message;

    [TestMethod]
    public void ReadConfig_BuildsConfig_ForValidInputs()
    {
        var config = Read();
        Assert.AreEqual("owner/repo", config.Repo.ToString());
        Assert.AreEqual("read-tok", config.ReadToken.Value);
        Assert.AreEqual(123, config.RunId);
    }

    [TestMethod]
    public void ReadConfig_RejectsEmptyRepo()
    => Assert.AreEqual("--repo is required", ErrorOf(() => Read(repo: "")));

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    public void ReadConfig_RejectsMalformedRepo(string repo)
    {
        var error = ErrorOf(() => Read(repo: repo));
        StringAssert.StartsWith(error, "--repo: ");
        StringAssert.Contains(error, "repo identifier");
    }

    [TestMethod]
    public void ReadConfig_RejectsEmptyReadToken()
    => Assert.AreEqual("--read-token is required", ErrorOf(() => Read(readToken: "")));

    [TestMethod]
    public void ReadConfig_RejectsEmptyRunId()
    => Assert.AreEqual("--run-id is required", ErrorOf(() => Read(runId: "")));

    [TestMethod]
    [DataRow("abc")]
    [DataRow("0")]
    [DataRow("-5")]
    public void ReadConfig_RejectsMalformedRunId(string runId)
    => Assert.AreEqual($"--run-id: must be a positive integer, got '{runId}'", ErrorOf(() => Read(runId: runId)));

    [TestMethod]
    public void ReadConfig_ReportsTheFirstProblemOnly()
    => Assert.AreEqual("--repo is required", ErrorOf(() => Read(repo: "", readToken: "", runId: "")));
}
