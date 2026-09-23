using Rix.CiFailure;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class CiFailureDetectorTests
{
    private static readonly RepoIdentifier Repo = new RepoIdentifier("owner/repo");
    private static readonly RunId Run = new(1);

    /// <summary>The cap is a parameter of every detection, so it is defaulted here rather than
    /// restated by the tests that aren't about the loop guard.</summary>
    private static Task<ICiFailureResult> Detect(StubCiFailureHost host, int maxRixCommits = CiFailureConfig.DefaultMaxRixCommits)
    => CiFailureDetector.DetectAsync(Repo, Run, host, new MaxRixCommits(maxRixCommits), CancellationToken.None);

    [TestMethod]
    public async Task DetectAsync_ReturnsSkipped_WhenRunDidNotFail()
    {
        var host = new StubCiFailureHost(getRun: _ => Task.FromResult(TestRuns.Sample("success")));

        var result = await Detect(host);

        var skipped = AssertSkipped(result);
        Assert.AreEqual("success", skipped.Conclusion);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsSkipped_WhenRunStillInProgress()
    {
        var host = new StubCiFailureHost(getRun: _ => Task.FromResult(TestRuns.Sample(conclusion: null)));

        var result = await Detect(host);

        var skipped = AssertSkipped(result);
        Assert.IsNull(skipped.Conclusion);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenGetRunFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => throw new RepositoryHostException("boom"));

        var result = await Detect(host);

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

        var result = await Detect(host);

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

        var result = await Detect(host);

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

        var result = await Detect(host);

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

        var result = await Detect(host);

        AssertError(result);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenPrLookupFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            findPr: _ => throw new RepositoryHostException("pr lookup failed"));

        var result = await Detect(host);

        AssertError(result);
    }

    /// <summary>The trust boundary: a fork's branch can be pushed to by anyone, so its logs must
    /// never reach the agent — which means they must never even be fetched.</summary>
    [TestMethod]
    public async Task DetectAsync_ReturnsUntrustedRun_WhenTheRunComesFromAFork()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure", branch: "patch-1", headRepo: "outsider/repo")),
            getLogs: _ => throw new AssertFailedException("a fork's logs must not be fetched at all"),
            countRixCommits: _ => throw new AssertFailedException("a fork's branch must not be inspected at all"));

        var result = await Detect(host);

        var untrusted = result switch
        {
            CiFailureUntrustedRun u => u,
            _ => throw new AssertFailedException($"expected CiFailureUntrustedRun, got {result}"),
        };
        Assert.AreEqual("outsider/repo", untrusted.HeadRepo);
        Assert.AreEqual("patch-1", untrusted.Branch);
    }

    /// <summary>GitHub treats owner and repo names case-insensitively, so the repo the run was
    /// checked against can be spelled differently from the one the API reports and still be it —
    /// turning rix off for a whole repo over a capital letter would be the worse failure.</summary>
    [TestMethod]
    public async Task DetectAsync_TreatsTheRepoAsItsOwn_WhenOnlyItsCasingDiffers()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure", headRepo: "Owner/Repo")));

        AssertDetected(await Detect(host));
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsLoopGuarded_WhenRixCommitsFillTheBranchTip()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            countRixCommits: _ => Task.FromResult(3));

        var result = await Detect(host, maxRixCommits: 3);

        var guarded = result switch
        {
            CiFailureLoopGuarded g => g,
            _ => throw new AssertFailedException($"expected CiFailureLoopGuarded, got {result}"),
        };
        Assert.AreEqual("rix/fix", guarded.Branch);
        Assert.AreEqual(3, guarded.RixCommits);
        Assert.AreEqual(3, host.MaxRixCommits?.Value, "the configured cap must reach the host, not a constant of its own");
        Assert.IsNull(host.TotalTailChars, "a guarded failure must not pay for the logs it will never use");
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsDetected_WhenTheRixCommitStreakIsShorterThanTheCap()
    {
        // One below the cap: the streak rix itself just added still leaves it a turn to take.
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            countRixCommits: _ => Task.FromResult(2));

        AssertDetected(await Detect(host, maxRixCommits: 3));
    }

    [TestMethod]
    public async Task DetectAsync_CountsTheStreakOnTheFailingRunsOwnBranch()
    {
        BranchName? counted = null;
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure", branch: "feature/x")),
            countRixCommits: branch =>
            {
                counted = branch;
                return Task.FromResult(0);
            });

        AssertDetected(await Detect(host));

        Assert.AreEqual("feature/x", counted?.Value);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenTheCommitCountFails()
    {
        var host = new StubCiFailureHost(
            getRun: _ => Task.FromResult(TestRuns.Sample("failure")),
            countRixCommits: _ => throw new RepositoryHostException("commit listing failed"));

        var error = AssertError(await Detect(host));

        StringAssert.Contains(error.Error, "commit listing failed");
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
