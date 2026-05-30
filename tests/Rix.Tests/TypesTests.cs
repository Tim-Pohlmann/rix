using System.Text.Json;

namespace Rix.Tests;

[TestClass]
public class TypesTests
{
    [TestMethod]
    [DataRow("rix/fix-bug", true)]
    [DataRow("rix/feature", true)]
    [DataRow("main", false)]
    [DataRow("feature/foo", false)]
    [DataRow("", false)]
    public void BranchName_IsValid(string value, bool expected)
    {
        Assert.AreEqual(expected, new BranchName(value).Valid);
    }

    [TestMethod]
    public void BranchName_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(new BranchName("rix/fix"));
        Assert.AreEqual("\"rix/fix\"", json);
    }

    [TestMethod]
    public void BranchName_DeserializesFromString()
    {
        var branch = JsonSerializer.Deserialize<BranchName>("\"rix/fix\"");
        Assert.AreEqual(new BranchName("rix/fix"), branch);
    }

    [TestMethod]
    public void JobSuccess_SerializesCorrectly()
    {
        var prs = new[] { new PullRequest(new Uri("https://github.com/o/r/pull/1"), new BranchName("rix/fix")) };
        var outcome = new JobSuccess(prs, TokensUsed: 1000, Duration: TimeSpan.FromSeconds(42));

        var json = JsonSerializer.Serialize<IJobResult>(outcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("success", root.GetProperty("status").GetString());
        Assert.AreEqual(1000, root.GetProperty("tokensUsed").GetInt32());
        Assert.AreEqual(42, root.GetProperty("durationSeconds").GetInt32());
        Assert.AreEqual(1, root.GetProperty("prs").GetArrayLength());
        Assert.AreEqual("https://github.com/o/r/pull/1", root.GetProperty("prs")[0].GetProperty("url").GetString());
        Assert.AreEqual("rix/fix", root.GetProperty("prs")[0].GetProperty("branch").GetString());
    }

    [TestMethod]
    public void JobFailure_SerializesCorrectly()
    {
        var outcome = new JobFailure("Something went wrong", TokensUsed: 500, Duration: TimeSpan.FromSeconds(10));

        var json = JsonSerializer.Serialize<IJobResult>(outcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("failure", root.GetProperty("status").GetString());
        Assert.AreEqual("Something went wrong", root.GetProperty("error").GetString());
        Assert.AreEqual(500, root.GetProperty("tokensUsed").GetInt32());
        Assert.AreEqual(10, root.GetProperty("durationSeconds").GetInt32());
    }
}
