using Rix.CiFailure;
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
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => _ = new RixBranchName(value));
        StringAssert.Contains(ex.Message, "rix/*");
    }

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void RepoIdentifier_RejectsInvalidFormat(string repo)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => _ = new RepoIdentifier(repo));
        StringAssert.Contains(ex.Message, "repo identifier");
    }

    [TestMethod]
    public void RepoIdentifier_AcceptsOwnerSlashRepo()
    {
        var repo = new RepoIdentifier("owner/repo");
        Assert.AreEqual("owner/repo", repo.Value);
        Assert.AreEqual("owner", repo.Owner);
    }

    [TestMethod]
    public void DirectoryPath_NormalisesRelativeToAbsolute()
    {
        var path = new DirectoryPath(".");
        Assert.IsTrue(Path.IsPathRooted(path.Value), $"expected an absolute path, got: {path.Value}");
        Assert.AreEqual(Path.GetFullPath("."), path.Value);
    }

    [TestMethod]
    public void DirectoryPath_RejectsNonExistent()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => _ = new DirectoryPath("/nonexistent/path/xyz"));
        Assert.AreEqual("directory does not exist: /nonexistent/path/xyz", ex.Message);
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
        // InvalidInputException from RixBranchName ctor should be wrapped as JsonException
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
    [DataRow(1)]
    [DataRow(MaxRixCommits.MaxValue)]
    public void MaxRixCommits_AcceptsBothEndsOfItsRange(int value)
    => Assert.AreEqual(value, new MaxRixCommits(value).Value);

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(MaxRixCommits.MaxValue + 1)]
    public void MaxRixCommits_RejectsValuesOutsideIt(int value)
    {
        // The upper bound is GitHub's page size: a cap above it could not be distinguished from
        // no cap at all, since the streak is read in a single request.
        var error = Assert.ThrowsExactly<InvalidInputException>(() => new MaxRixCommits(value).ToString()).Message;
        StringAssert.Contains(error, "must be between 1 and 100");
    }

    /// <summary>The discriminator the composite action switches on: run-ci-failure/action.yml reads
    /// `.status` and treats anything it doesn't recognize as a broken result, so renaming this
    /// silently turns a guarded run into a reported error.</summary>
    [TestMethod]
    public void CiFailureLoopGuarded_SerializesWithLoopGuardedStatus()
    {
        var json = JsonSerializer.Serialize<ICiFailureResult>(new CiFailureLoopGuarded("rix/fix", 5), CiFailureJsonContext.Default.ICiFailureResult);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("loopGuarded", root.GetProperty("status").GetString());
        Assert.AreEqual("rix/fix", root.GetProperty("branch").GetString());
        Assert.AreEqual(5, root.GetProperty("rixCommits").GetInt32());
    }

    [TestMethod]
    public void JobSuccess_SerializesCorrectly()
    {
        var prs = new[] { new PendingPr(new RixBranchName("rix/fix"), new BranchName("main"), new PrTitle("Fix bug"), new PrBody("body"), "rix-fix.bundle") };
        var outcome = new JobSuccess(prs, PendingPushRequests: [], CostUsd: 0.0125m, Duration: TimeSpan.FromSeconds(42));

        var json = JsonSerializer.Serialize<IJobResult>(outcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("success", root.GetProperty("status").GetString());
        Assert.AreEqual(0.0125m, root.GetProperty("costUsd").GetDecimal());
        Assert.AreEqual(42, root.GetProperty("durationSeconds").GetInt32());
        Assert.AreEqual(1, root.GetProperty("pendingPrRequests").GetArrayLength());
        Assert.AreEqual("rix/fix", root.GetProperty("pendingPrRequests")[0].GetProperty("branch").GetString());
        Assert.AreEqual("rix-fix.bundle", root.GetProperty("pendingPrRequests")[0].GetProperty("bundleFile").GetString());
        Assert.AreEqual(0, root.GetProperty("pendingPushRequests").GetArrayLength());
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
