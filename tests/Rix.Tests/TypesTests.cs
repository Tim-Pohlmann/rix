using Rix.Job;
using System.Text.Json;

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
    public void BranchName_DeserializeNonString_ThrowsJsonException()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<BranchName>("42"));
    }

    [TestMethod]
    public void RixBranchName_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(new RixBranchName("rix/fix"));
        Assert.AreEqual("\"rix/fix\"", json);
    }

    [TestMethod]
    public void RixBranchName_DeserializesFromString()
    {
        var branch = JsonSerializer.Deserialize<RixBranchName>("\"rix/fix\"");
        Assert.AreEqual(new RixBranchName("rix/fix"), branch);
    }

    [TestMethod]
    public void RixBranchName_DeserializeInvalidValue_ThrowsJsonException()
    {
        // ArgumentException from RixBranchName ctor should be wrapped as JsonException
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<RixBranchName>("\"main\""));
    }

    [TestMethod]
    public void RixBranchName_DeserializeNonString_ThrowsJsonException()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<RixBranchName>("42"));
    }

    [TestMethod]
    public void PrTitle_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(new PrTitle("Fix bug"));
        Assert.AreEqual("\"Fix bug\"", json);
    }

    [TestMethod]
    public void PrTitle_DeserializesFromString()
    {
        var title = JsonSerializer.Deserialize<PrTitle>("\"Fix bug\"");
        Assert.AreEqual(new PrTitle("Fix bug"), title);
    }

    [TestMethod]
    public void PrTitle_DeserializeNonString_ThrowsJsonException()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<PrTitle>("42"));
    }

    [TestMethod]
    public void PrBody_SerializesAsString()
    {
        var json = JsonSerializer.Serialize(new PrBody("body text"));
        Assert.AreEqual("\"body text\"", json);
    }

    [TestMethod]
    public void PrBody_DeserializesFromString()
    {
        var body = JsonSerializer.Deserialize<PrBody>("\"body text\"");
        Assert.AreEqual(new PrBody("body text"), body);
    }

    [TestMethod]
    public void PrBody_DeserializeNonString_ThrowsJsonException()
    {
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<PrBody>("42"));
    }

    [TestMethod]
    public void JobSuccess_SerializesCorrectly()
    {
        var prs = new[] { new PendingPr(new RixBranchName("rix/fix"), new BranchName("main"), new PrTitle("Fix bug"), new PrBody("body"), "rix-fix.bundle") };
        var outcome = new JobSuccess(prs, CostUsd: 0.0125m, Duration: TimeSpan.FromSeconds(42));

        var json = JsonSerializer.Serialize<IJobResult>(outcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("success", root.GetProperty("status").GetString());
        Assert.AreEqual(0.0125m, root.GetProperty("costUsd").GetDecimal());
        Assert.AreEqual(42, root.GetProperty("durationSeconds").GetInt32());
        Assert.AreEqual(1, root.GetProperty("pendingPrRequests").GetArrayLength());
        Assert.AreEqual("rix/fix", root.GetProperty("pendingPrRequests")[0].GetProperty("branch").GetString());
        Assert.AreEqual("rix-fix.bundle", root.GetProperty("pendingPrRequests")[0].GetProperty("bundleFile").GetString());
    }

    [TestMethod]
    public void JobFailure_SerializesCorrectly()
    {
        var outcome = new JobFailure("Something went wrong", CostUsd: 0.005m, Duration: TimeSpan.FromSeconds(10));

        var json = JsonSerializer.Serialize<IJobResult>(outcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("failure", root.GetProperty("status").GetString());
        Assert.AreEqual("Something went wrong", root.GetProperty("error").GetString());
        Assert.AreEqual(0.005m, root.GetProperty("costUsd").GetDecimal());
        Assert.AreEqual(10, root.GetProperty("durationSeconds").GetInt32());
    }

    [TestMethod]
    public void SetupFailure_SerializesWithSetupFailureStatus()
    {
        var outcome = new SetupFailure("Claude install failed: nope");

        var json = JsonSerializer.Serialize<IJobResult>(outcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("setupFailure", root.GetProperty("status").GetString());
        Assert.AreEqual("Claude install failed: nope", root.GetProperty("error").GetString());
        Assert.AreEqual(0m, root.GetProperty("costUsd").GetDecimal());
        Assert.AreEqual(0, root.GetProperty("durationSeconds").GetInt32());
    }
}
