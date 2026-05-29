using System.Text.Json;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobRunnerTests
{
    [TestMethod]
    public async Task RunAsync_Returns2_WhenConfigIsInvalid()
    {
        var config = JobConfig.FromInputs(
            repo: "",
            prompt: "",
            readToken: "",
            writeToken: "",
            maxTokens: null,
            timeoutMinutes: null,
            workDir: null);

        var exitCode = await Startup.RunAsync(["job"]);
        Assert.AreEqual(2, exitCode);
    }

    [TestMethod]
    public void JobSuccess_SerializesCorrectly()
    {
        var prs = new[] { new PrInfo("https://github.com/o/r/pull/1", "rix/fix") };
        var outcome = new JobSuccess(prs, TokensUsed: 1000, DurationSeconds: 42);

        var json = JsonSerializer.Serialize(outcome, JobJsonContext.Default.JobOutcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("success", root.GetProperty("status").GetString());
        Assert.AreEqual(1000, root.GetProperty("tokensUsed").GetInt32());
        Assert.AreEqual(42, root.GetProperty("durationSeconds").GetInt32());
        Assert.AreEqual(1, root.GetProperty("prs").GetArrayLength());
    }

    [TestMethod]
    public void JobFailure_SerializesCorrectly()
    {
        var outcome = new JobFailure("Something went wrong", TokensUsed: 500, DurationSeconds: 10);

        var json = JsonSerializer.Serialize(outcome, JobJsonContext.Default.JobOutcome);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.AreEqual("failure", root.GetProperty("status").GetString());
        Assert.AreEqual("Something went wrong", root.GetProperty("error").GetString());
        Assert.AreEqual(500, root.GetProperty("tokensUsed").GetInt32());
    }
}
