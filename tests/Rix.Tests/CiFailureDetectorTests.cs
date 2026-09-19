using Rix.CiFailure;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class CiFailureDetectorTests
{
    private static readonly RepoIdentifier Repo = new RepoIdentifier("owner/repo");
    private static readonly RunId Run = new(1);

    [TestMethod]
    public async Task DetectAsync_ReturnsSkipped_WhenRunDidNotFail()
    {
        var host = new StubCiFailureHost(getRun: _ => Task.FromResult(TestRuns.Sample("success")));

        var result = await CiFailureDetector.DetectAsync(Repo, Run, host, CancellationToken.None);

        var skipped = AssertSkipped(result);
        Assert.AreEqual("success", skipped.Conclusion);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsSkipped_WhenRunStillInProgress()
    {
        var host = new StubCiFailureHost(getRun: _ => Task.FromResult(TestRuns.Sample(conclusion: null)));

        var result = await CiFailureDetector.DetectAsync(Repo, Run, host, CancellationToken.None);

        var skipped = AssertSkipped(result);
        Assert.IsNull(skipped.Conclusion);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenGetRunFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => throw new RepositoryHostException("boom"));

        var result = await CiFailureDetector.DetectAsync(Repo, Run, host, CancellationToken.None);

        var error = AssertError(result);
        StringAssert.Contains(error.Error, "boom");
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsDetected_WithPromptAndFacts_WhenRunFailed()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            getLogs: _ => Task.FromResult("boom: it broke"),
            findPr: _ => Task.FromResult<int?>(7));

        var result = await CiFailureDetector.DetectAsync(Repo, Run, host, CancellationToken.None);

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
    public async Task DetectAsync_OmitsPrLine_WhenNoOpenPr()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            findPr: _ => Task.FromResult<int?>(null));

        var result = await CiFailureDetector.DetectAsync(Repo, Run, host, CancellationToken.None);

        var detected = AssertDetected(result);
        Assert.IsNull(detected.PrNumber);
        Assert.IsFalse(detected.Prompt.Contains("This is PR"));
    }

    /// <summary>Capping is the host's job, applied while streaming so a multi-MB log is never fully
    /// in memory. Trimming what comes back a second time here would only ever cut into an excerpt
    /// the host already sized to fit — leaving a headless first block — so the budget is handed
    /// down and the result embedded as-is.</summary>
    [TestMethod]
    public async Task DetectAsync_PassesItsLogBudgetToTheHost_AndEmbedsTheExcerptAsIs()
    {
        var excerpt = "===== build =====\nTAIL-MARKER";
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            getLogs: _ => Task.FromResult(excerpt));

        var result = await CiFailureDetector.DetectAsync(Repo, Run, host, CancellationToken.None);

        var detected = AssertDetected(result);
        StringAssert.Contains(detected.Prompt, excerpt);
        Assert.IsTrue(host.TotalTailChars > 0, "a cap must reach the host, so a huge log is never fully held in memory");
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenLogFetchFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            getLogs: _ => throw new RepositoryHostException("log fetch failed"));

        var result = await CiFailureDetector.DetectAsync(Repo, Run, host, CancellationToken.None);

        AssertError(result);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenPrLookupFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            findPr: _ => throw new RepositoryHostException("pr lookup failed"));

        var result = await CiFailureDetector.DetectAsync(Repo, Run, host, CancellationToken.None);

        AssertError(result);
    }

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
