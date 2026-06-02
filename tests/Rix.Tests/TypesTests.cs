using System.Text.Json;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class TypesTests
{
    [TestMethod]
    [DataRow("rix/fix-bug")]
    [DataRow("rix/feature")]
    public void RixBranchName_AcceptsValidValues(string value)
    {
        var branch = new RixBranchName(value);
        Assert.AreEqual(value, branch.Value);
    }

    [TestMethod]
    [DataRow("main")]
    [DataRow("feature/foo")]
    [DataRow("")]
    public void RixBranchName_ThrowsOnInvalidValues(string value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RixBranchName(value));
    }

    [TestMethod]
    public void BranchName_AcceptsAnyString()
    {
        Assert.AreEqual("main", new BranchName("main").Value);
        Assert.AreEqual("rix/fix", new BranchName("rix/fix").Value);
        Assert.AreEqual("", new BranchName("").Value);
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
        var outcome = new JobSuccess(TokensUsed: 1000, Duration: TimeSpan.FromSeconds(42));

        var json = JsonSerializer.Serialize<IJobResult>(outcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("success", root.GetProperty("status").GetString());
        Assert.AreEqual(1000, root.GetProperty("tokensUsed").GetInt32());
        Assert.AreEqual(42, root.GetProperty("durationSeconds").GetInt32());
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
