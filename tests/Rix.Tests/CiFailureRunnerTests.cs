using Rix.CiFailure;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class CiFailureRunnerTests
{
    private static readonly CiFailureConfig Config = TestConfig.ValidCiFailure();

    [TestMethod]
    public async Task RunAsync_ReturnsSkipped_WhenRunDidNotFail()
    {
        var host = new StubCiFailureHost(getRun: _ => Task.FromResult(SampleRun("success")));

        var result = await CiFailureRunner.RunAsync(Config, host, CancellationToken.None);

        var skipped = AssertSkipped(result);
        Assert.AreEqual("success", skipped.Conclusion);
    }

    [TestMethod]
    public async Task RunAsync_ReturnsError_WhenGetRunFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => throw new HttpRequestException("boom"));

        var result = await CiFailureRunner.RunAsync(Config, host, CancellationToken.None);

        var error = AssertError(result);
        StringAssert.Contains(error.Error, "boom");
    }

    [TestMethod]
    public async Task RunAsync_ReturnsDetected_WithPromptAndFacts_WhenRunFailed()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure")),
            getLogs: _ => Task.FromResult("boom: it broke"),
            findPr: _ => Task.FromResult<int?>(7));

        var result = await CiFailureRunner.RunAsync(Config, host, CancellationToken.None);

        var detected = AssertDetected(result);
        Assert.AreEqual("https://github.com/owner/repo/actions/runs/1", detected.RunUrl);
        Assert.AreEqual("rix/fix", detected.Branch);
        Assert.AreEqual(7, detected.PrNumber);
        StringAssert.Contains(detected.Prompt, "CI failed on branch 'rix/fix'");
        StringAssert.Contains(detected.Prompt, "This is PR #7 in owner/repo.");
        StringAssert.Contains(detected.Prompt, "Fix thing");
        StringAssert.Contains(detected.Prompt, "boom: it broke");
    }

    [TestMethod]
    public async Task RunAsync_OmitsPrLine_WhenNoOpenPr()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure")),
            findPr: _ => Task.FromResult<int?>(null));

        var result = await CiFailureRunner.RunAsync(Config, host, CancellationToken.None);

        var detected = AssertDetected(result);
        Assert.IsNull(detected.PrNumber);
        Assert.IsFalse(detected.Prompt.Contains("This is PR"));
    }

    [TestMethod]
    public async Task RunAsync_TruncatesLogsToTail_WhenTooLong()
    {
        var hugeLog = new string('x', 25_000) + "TAIL-MARKER";
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure")),
            getLogs: _ => Task.FromResult(hugeLog));

        var result = await CiFailureRunner.RunAsync(Config, host, CancellationToken.None);

        var detected = AssertDetected(result);
        StringAssert.Contains(detected.Prompt, "TAIL-MARKER");
        Assert.IsTrue(detected.Prompt.Length < hugeLog.Length + 500, "log excerpt must be capped, not passed through whole");
    }

    [TestMethod]
    public async Task RunAsync_ReturnsError_WhenLogFetchFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure")),
            getLogs: _ => throw new HttpRequestException("log fetch failed"));

        var result = await CiFailureRunner.RunAsync(Config, host, CancellationToken.None);

        AssertError(result);
    }

    [TestMethod]
    public async Task RunAsync_ReturnsError_WhenPrLookupFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure")),
            findPr: _ => throw new HttpRequestException("pr lookup failed"));

        var result = await CiFailureRunner.RunAsync(Config, host, CancellationToken.None);

        AssertError(result);
    }

    private static WorkflowRun SampleRun(string conclusion)
    => new(conclusion, "Fix thing", "https://github.com/owner/repo/actions/runs/1", "rix/fix");

    private static CiFailureDetected AssertDetected(ICiFailureResult result) => result switch
    {
        CiFailureDetected d => d,
        _ => throw new AssertFailedException($"expected CiFailureDetected, got {result}"),
    };

    private static CiFailureSkipped AssertSkipped(ICiFailureResult result) => result switch
    {
        CiFailureSkipped s => s,
        _ => throw new AssertFailedException($"expected CiFailureSkipped, got {result}"),
    };

    private static CiFailureError AssertError(ICiFailureResult result) => result switch
    {
        CiFailureError e => e,
        _ => throw new AssertFailedException($"expected CiFailureError, got {result}"),
    };
}
