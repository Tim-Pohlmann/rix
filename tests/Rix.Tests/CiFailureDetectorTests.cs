using System.Text.Json;
using Rix.CiFailure;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class CiFailureDetectorTests
{
    private static readonly RepoIdentifier Repo = new RepoIdentifier("owner/repo");
    private static readonly RunId Run = new(1);

    /// <summary>Only the shell's tests need this; the detector writes nothing.</summary>
    private string _outputDir = null!;

    [TestInitialize]
    public void Setup() => _outputDir = Directory.CreateTempSubdirectory("rix-out-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_outputDir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    /// <summary>The cap is a parameter of every detection, so it is defaulted here rather than
    /// restated by the tests that aren't about the loop guard. The repo host is defaulted too: most
    /// of these are about reading the run, and an unstubbed one answers "no open PR, no rix commits"
    /// rather than needing to be spelled out.</summary>
    private static Task<ICiFailureResult> Detect(
        StubCiHost ci,
        StubCiFailureRepoHost? repoHost = null,
        int maxRixCommits = CiFailureConfig.DefaultMaxRixCommits)
    => CiFailureDetector.DetectAsync(Repo, Run, ci, repoHost ?? new StubCiFailureRepoHost(), new MaxRixCommits(maxRixCommits), CancellationToken.None);

    [TestMethod]
    public async Task DetectAsync_ReturnsSkipped_WhenRunDidNotFail()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiSucceeded())));

        var result = await Detect(ci);

        var skipped = AssertSkipped(result);
        Assert.AreEqual("succeeded", skipped.Outcome);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsSkipped_WhenRunStillInProgress()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiPending())));

        var result = await Detect(ci);

        var skipped = AssertSkipped(result);
        Assert.AreEqual("pending", skipped.Outcome);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenGetRunFails()
    {
        var ci = new StubCiHost(
            getRun: _ => throw new CiHostException("boom"));

        var result = await Detect(ci);

        var error = AssertError(result);
        StringAssert.Contains(error.Error, "boom");
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsDetected_WithPromptAndFacts_WhenRunFailed()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())),
            getLogs: _ => Task.FromResult("boom: it broke"));
        var repoHost = new StubCiFailureRepoHost(findPr: _ => Task.FromResult<int?>(7));

        var result = await Detect(ci, repoHost);

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
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())));
        var repoHost = new StubCiFailureRepoHost(findPr: _ => Task.FromResult<int?>(null));

        var result = await Detect(ci, repoHost);

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
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())),
            getLogs: _ => Task.FromResult(excerpt));

        var result = await Detect(ci);

        var detected = AssertDetected(result);
        StringAssert.Contains(detected.Prompt, excerpt);
        Assert.IsTrue(ci.TotalTailChars > 0, "a cap must reach the host, so a huge log is never fully held in memory");
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenLogFetchFails()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())),
            getLogs: _ => throw new CiHostException("log fetch failed"));

        var result = await Detect(ci);

        AssertError(result);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenPrLookupFails()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())));
        var repoHost = new StubCiFailureRepoHost(findPr: _ => throw new RepoHostException("pr lookup failed"));

        var result = await Detect(ci, repoHost);

        AssertError(result);
    }

    /// <summary>The trust boundary: a fork's branch can be pushed to by anyone, so its logs must
    /// never reach the agent — which means they must never even be fetched.</summary>
    [TestMethod]
    public async Task DetectAsync_ReturnsUntrustedRun_WhenTheRunComesFromAFork()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), branch: "patch-1", headRepo: "outsider/repo")),
            getLogs: _ => throw new AssertFailedException("a fork's logs must not be fetched at all"));
        var repoHost = new StubCiFailureRepoHost(
            countRixCommits: _ => throw new AssertFailedException("a fork's branch must not be inspected at all"));

        var result = await Detect(ci, repoHost);

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
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), headRepo: "Owner/Repo")));

        AssertDetected(await Detect(ci));
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsLoopGuarded_WhenRixCommitsFillTheBranchTip()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())));
        var repoHost = new StubCiFailureRepoHost(countRixCommits: _ => Task.FromResult(3));

        var result = await Detect(ci, repoHost, maxRixCommits: 3);

        var guarded = result switch
        {
            CiFailureLoopGuarded g => g,
            _ => throw new AssertFailedException($"expected CiFailureLoopGuarded, got {result}"),
        };
        Assert.AreEqual("rix/fix", guarded.Branch);
        Assert.AreEqual(3, guarded.RixCommits);
        Assert.AreEqual(3, repoHost.MaxRixCommits?.Value, "the configured cap must reach the host, not a constant of its own");
        Assert.IsNull(ci.TotalTailChars, "a guarded failure must not pay for the logs it will never use");
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsDetected_WhenTheRixCommitStreakIsShorterThanTheCap()
    {
        // One below the cap: the streak rix itself just added still leaves it a turn to take.
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())));
        var repoHost = new StubCiFailureRepoHost(countRixCommits: _ => Task.FromResult(2));

        AssertDetected(await Detect(ci, repoHost, maxRixCommits: 3));
    }

    [TestMethod]
    public async Task DetectAsync_CountsTheStreakOnTheFailingRunsOwnBranch()
    {
        BranchName? counted = null;
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), branch: "feature/x")));
        var repoHost = new StubCiFailureRepoHost(countRixCommits: branch =>
        {
            counted = branch;
            return Task.FromResult(0);
        });

        AssertDetected(await Detect(ci, repoHost));

        Assert.AreEqual("feature/x", counted?.Value);
    }

    [TestMethod]
    public async Task DetectAsync_ReturnsError_WhenTheCommitCountFails()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())));
        var repoHost = new StubCiFailureRepoHost(countRixCommits: _ => throw new RepoHostException("commit listing failed"));

        var error = AssertError(await Detect(ci, repoHost));

        StringAssert.Contains(error.Error, "commit listing failed");
    }

    /// <summary>The imperative shell's own tests, beside the core's like
    /// <c>JobRunnerTests</c>'s are: <see cref="Startup.ExecuteCiFailureAsync"/> adds exactly two
    /// things to a detection - the exit code it maps the verdict to, and the files it drops in the
    /// output directory - and both are only meaningful next to the verdict that produced
    /// them.</summary>
    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns0_AndWritesTheVerdict_WhenRunDidNotFail()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiSucceeded())));

        var exitCode = await Startup.ExecuteCiFailureAsync(
            Config(), CancellationToken.None, new CiFailureContext(ci, new StubCiFailureRepoHost()));

        Assert.AreEqual(0, exitCode);
        // Written for every verdict, not only the actionable one: the caller gating a workflow on
        // this needs to read a status either way, and "no file" is not a status.
        Assert.AreEqual("skipped", StatusOfWrittenResult());
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "prompt.md")));
    }

    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns0_AndWritesTheVerdict_WhenGuardedAgainstALoop()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())));
        var repoHost = new StubCiFailureRepoHost(countRixCommits: _ => Task.FromResult(CiFailureConfig.DefaultMaxRixCommits));

        // Exit 0, like any other reason not to act: the branch being rix's own work is a decision,
        // not a failure of this run, and a non-zero exit would fail the caller's workflow for it.
        var exitCode = await Startup.ExecuteCiFailureAsync(
            Config(), CancellationToken.None, new CiFailureContext(ci, repoHost));

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("loopGuarded", StatusOfWrittenResult());
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "prompt.md")));
    }

    /// <summary>Exit 0, like every other reason not to act: a fork PR failing CI is the normal
    /// course of events, not a broken rix run for the repo's Actions tab to go red over.</summary>
    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns0_AndWritesTheVerdict_WhenTheFailureComesFromAFork()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), headRepo: "outsider/repo")));

        var exitCode = await Startup.ExecuteCiFailureAsync(
            Config(), CancellationToken.None, new CiFailureContext(ci, new StubCiFailureRepoHost()));

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("untrustedRun", StatusOfWrittenResult());
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "prompt.md")));
    }

    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns1_WhenCiFailureCheckErrors()
    {
        var ci = new StubCiHost(getRun: _ => throw new CiHostException("boom"));

        var exitCode = await Startup.ExecuteCiFailureAsync(
            Config(), CancellationToken.None, new CiFailureContext(ci, new StubCiFailureRepoHost()));

        Assert.AreEqual(1, exitCode);
        Assert.AreEqual("error", StatusOfWrittenResult());
    }

    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns0_AndWritesThePromptBesideTheVerdict_WhenRunFailed()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), branch: "rix/fix")),
            getLogs: _ => Task.FromResult("boom: it broke"));

        var exitCode = await Startup.ExecuteCiFailureAsync(
            Config(), CancellationToken.None, new CiFailureContext(ci, new StubCiFailureRepoHost()));

        Assert.AreEqual(0, exitCode);
        Assert.AreEqual("detected", StatusOfWrittenResult());
        // The one file carrying the failing run's own log text, kept out of the JSON's way so that
        // whoever hands it to an agent never has to quote it back out of a parsed field.
        var prompt = await File.ReadAllTextAsync(Path.Combine(_outputDir, "prompt.md"));
        StringAssert.Contains(prompt, "CI failed on branch 'rix/fix'");
        StringAssert.Contains(prompt, "boom: it broke");
    }

    private CiFailureConfig Config() => TestConfig.ValidCiFailure(outputDir: _outputDir);

    private string? StatusOfWrittenResult()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_outputDir, "result.json")));
        return doc.RootElement.GetProperty("status").GetString();
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
