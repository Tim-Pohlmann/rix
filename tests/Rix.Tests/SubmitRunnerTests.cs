using Rix.Process;
using Rix.Submit;

namespace Rix.Tests;

[TestClass]
public class SubmitRunnerTests
{
    private static readonly string[] ExpectedPushedBranches = ["rix/my-fix"];

    private string _inputDir = null!;
    private string _workDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _inputDir = Directory.CreateTempSubdirectory("rix-submit-in-").FullName;
        _workDir = Directory.CreateTempSubdirectory("rix-submit-work-").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_inputDir, recursive: true); } catch (DirectoryNotFoundException) { }
        try { Directory.Delete(_workDir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    [TestMethod]
    public async Task RunAsync_Fails_WhenResultJsonMissing()
    {
        var result = await Run(new StubSubmitHost());

        AssertFailure(result, "result.json not found");
    }

    [TestMethod]
    public async Task RunAsync_Fails_WhenJobWasNotSuccessful()
    {
        WriteResultJson("""{"status":"failure","error":"boom","costUsd":0,"durationSeconds":1}""");

        var result = await Run(new StubSubmitHost());

        AssertFailure(result, "does not describe a successful job");
    }

    [TestMethod]
    public async Task RunAsync_Succeeds_WithNoPullRequests()
    {
        WriteResultJson("""{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}""");
        var host = new StubSubmitHost();

        var result = await Run(host);

        var success = AssertSuccess(result);
        Assert.AreEqual(0, success.CreatedPrs.Count);
        Assert.AreEqual(0, success.PushedBranches.Count);
        Assert.IsFalse(host.CloneCalled, "must not clone when there is nothing to push");
    }

    [TestMethod]
    public async Task RunAsync_PushesCommitsToExistingBranch_WhenOnlyPushQueued()
    {
        WriteOnePendingPush();
        var host = new StubSubmitHost();
        var commands = new List<string>();

        var result = await Run(host, GitRunner(commands));

        var success = AssertSuccess(result);
        Assert.AreEqual(0, success.CreatedPrs.Count, "a push must not open a PR");
        CollectionAssert.AreEqual(ExpectedPushedBranches, success.PushedBranches.ToArray());
        CollectionAssert.AreEqual(ExpectedPushedBranches, host.PushedBranches.Select(b => b.Value).ToArray());
        CollectionAssert.Contains(commands, "fetch");
    }

    [TestMethod]
    public async Task RunAsync_PushesToBranchThatExistsOnRemote_DoesNotFailFast()
    {
        // The branch already existing on the remote is the whole point of a push, so the submit
        // guard that fails a PR for that reason must not apply here.
        WriteOnePendingPush();
        var host = new StubSubmitHost(branchExists: _ => Task.FromResult(true));

        var result = await Run(host);

        AssertSuccess(result);
        CollectionAssert.AreEqual(ExpectedPushedBranches, host.PushedBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public async Task RunAsync_PushesAndOpensPr_ForBothPrAndPush()
    {
        WriteResultJson(OnePrAndOnePushJson());
        File.WriteAllText(Path.Combine(_inputDir, "rix_2Fmy-fix.bundle"), "fake-bundle");
        var host = new StubSubmitHost();

        var result = await Run(host);

        var success = AssertSuccess(result);
        Assert.AreEqual(1, host.CreatedPrs.Count);
        Assert.AreEqual(1, success.CreatedPrs.Count);
        CollectionAssert.AreEqual(ExpectedPushedBranches, success.PushedBranches.ToArray());
        Assert.AreEqual(2, host.PushedBranches.Count, "both the PR branch and the pushed branch are pushed");
    }

    [TestMethod]
    public async Task RunAsync_Fails_WhenPushBundleFileMissing()
    {
        WriteResultJson(OnePendingPushJson(bundleFile: "missing.bundle"));

        var result = await Run(new StubSubmitHost());

        AssertFailure(result, "bundle file not found");
    }

    [TestMethod]
    public async Task RunAsync_Fails_WhenPushGitPushFails()
    {
        WriteOnePendingPush();
        var host = new StubSubmitHost(
            pushBranch: _ => throw new InvalidOperationException("exited with code 1"));

        var result = await Run(host);

        AssertFailure(result, "git push failed");
    }

    [TestMethod]
    public async Task RunAsync_PushesAndOpensPr_ForEachPending()
    {
        WriteOnePendingPr();
        var host = new StubSubmitHost();
        var commands = new List<string>();

        var result = await Run(host, GitRunner(commands));

        var success = AssertSuccess(result);
        Assert.AreEqual(1, host.CreatedPrs.Count);
        Assert.AreEqual("rix/my-fix", host.CreatedPrs[0].Branch.Value);
        Assert.AreEqual(1, success.CreatedPrs.Count);
        Assert.AreEqual("rix/my-fix", success.CreatedPrs[0].Branch);
        Assert.AreEqual("https://github.com/owner/repo/pull/1", success.CreatedPrs[0].Url);
        CollectionAssert.Contains(commands, "fetch");
        CollectionAssert.AreEqual(
            ExpectedPushedBranches, host.PushedBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public async Task RunAsync_Fails_AndDoesNotPushOrOpenPr_WhenBranchAlreadyExists()
    {
        WriteOnePendingPr();
        var host = new StubSubmitHost(branchExists: _ => Task.FromResult(true));
        var commands = new List<string>();

        var result = await Run(host, GitRunner(commands));

        AssertFailure(result, "branch already exists on remote");
        Assert.AreEqual(0, host.CreatedPrs.Count);
        Assert.AreEqual(0, commands.Count, "must not touch git when the branch already exists");
    }

    [TestMethod]
    public async Task RunAsync_Fails_WhenBundleFileMissing()
    {
        WriteResultJson(OnePendingPrJson(bundleFile: "missing.bundle"));

        var result = await Run(new StubSubmitHost());

        AssertFailure(result, "bundle file not found");
    }

    [TestMethod]
    public async Task RunAsync_Fails_WhenGitPushFails()
    {
        WriteOnePendingPr();
        var host = new StubSubmitHost(
            pushBranch: _ => throw new InvalidOperationException("exited with code 1"));

        var result = await Run(host);

        AssertFailure(result, "git push failed");
        Assert.AreEqual(0, host.CreatedPrs.Count);
    }

    [TestMethod]
    public async Task RunAsync_RefusesThePush_WhenItsBranchIsNotInTheAllowList()
    {
        WriteOnePendingPush();
        var host = new StubSubmitHost();

        var result = await Run(host, allowedPushBranches: "main,release");

        AssertFailure(result, "branch is not allowed to be pushed to: rix/my-fix");
        Assert.AreEqual(0, host.PushedBranches.Count, "a refused push must reach the remote in no form");
    }

    [TestMethod]
    public async Task RunAsync_RefusesEveryPush_WhenTheAllowListIsEmpty()
    {
        WriteOnePendingPush();
        var host = new StubSubmitHost();

        var result = await Run(host, allowedPushBranches: null);

        AssertFailure(result, "branch is not allowed to be pushed to");
        Assert.AreEqual(0, host.PushedBranches.Count);
    }

    /// <summary>The allow-list decides before anything reads the bundle the agent wrote, so a
    /// forged request is refused on the branch it names rather than on whatever its payload turns
    /// out to be — the bundle here does not exist at all, and the branch is still the reason given.</summary>
    [TestMethod]
    public async Task RunAsync_RefusesTheDisallowedPush_BeforeTouchingItsBundle()
    {
        WriteResultJson(OnePendingPushJson(bundleFile: "missing.bundle"));
        var gitCommands = new List<string>();

        var result = await Run(new StubSubmitHost(), GitRunner(gitCommands), allowedPushBranches: "main");

        AssertFailure(result, "branch is not allowed to be pushed to: rix/my-fix");
        Assert.AreEqual(0, gitCommands.Count, "nothing may be fetched from a bundle for a refused push");
    }

    /// <summary>The allow-list bounds pushes onto existing branches only. A pending PR names a
    /// branch that cannot exist yet and is bounded instead by RixBranchName's rix/* rule, which
    /// result.json re-applies on deserialization — so an operator who allows no pushes at all still
    /// gets their PRs.</summary>
    [TestMethod]
    public async Task RunAsync_OpensPullRequests_EvenWhenNoPushBranchIsAllowed()
    {
        WriteOnePendingPr();
        var host = new StubSubmitHost();

        var result = await Run(host, allowedPushBranches: null);

        var success = AssertSuccess(result);
        Assert.AreEqual(1, success.CreatedPrs.Count);
        CollectionAssert.AreEqual(ExpectedPushedBranches, host.PushedBranches.Select(b => b.Value).ToArray());
    }

    // ---- helpers ----

    /// <summary>Defaults to allowing the branch every push fixture here uses, so a test that isn't
    /// about the allow-list doesn't have to restate it. The deny cases pass their own list.</summary>
    private Task<ISubmitResult> Run
    (
        StubSubmitHost host,
        RunProcessAsync? runner = null,
        string? allowedPushBranches = "rix/my-fix"
    )
    => SubmitRunner.RunAsync
    (
        TestConfig.ValidSubmit(inputDir: _inputDir, workDir: _workDir, allowedPushBranches: allowedPushBranches),
        new SubmitContext(host, runner ?? OkGit, _ => { }),
        CancellationToken.None
    );

    private static readonly RunProcessAsync OkGit =
        (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessSuccess());

    private static RunProcessAsync GitRunner(List<string> commands) =>
        (_, args, _, _, _, _) =>
        {
            commands.Add(args.ElementAt(2)); // git -C <dir> <verb> ...
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

    private static SubmitSuccess AssertSuccess(ISubmitResult result)
    {
        if (result is SubmitSuccess success)
            return success;
        throw new AssertFailedException($"expected SubmitSuccess, got {result}");
    }

    private static void AssertFailure(ISubmitResult result, string expectedSubstring)
    {
        if (result is not SubmitFailure failure)
            throw new AssertFailedException($"expected SubmitFailure, got {result}");
        StringAssert.Contains(failure.Error, expectedSubstring);
    }

    private void WriteResultJson(string json) => File.WriteAllText(Path.Combine(_inputDir, "result.json"), json);

    private void WriteOnePendingPr()
    {
        WriteResultJson(OnePendingPrJson(bundleFile: "rix_2Fmy-fix.bundle"));
        File.WriteAllText(Path.Combine(_inputDir, "rix_2Fmy-fix.bundle"), "fake-bundle");
    }

    private void WriteOnePendingPush()
    {
        WriteResultJson(OnePendingPushJson(bundleFile: "rix_2Fmy-fix.bundle"));
        File.WriteAllText(Path.Combine(_inputDir, "rix_2Fmy-fix.bundle"), "fake-bundle");
    }

    private static string OnePendingPrJson(string bundleFile) =>
        $$"""
        {"status":"success","pendingPrRequests":[{"branch":"rix/my-fix","baseBranch":"main","title":"My fix","body":"body","bundleFile":"{{bundleFile}}"}],"costUsd":0,"durationSeconds":1}
        """;

    private static string OnePendingPushJson(string bundleFile) =>
        $$"""
        {"status":"success","pendingPrRequests":[],"pendingPushRequests":[{"branch":"rix/my-fix","baseBranch":"main","bundleFile":"{{bundleFile}}"}],"costUsd":0,"durationSeconds":1}
        """;

    private static string OnePrAndOnePushJson() =>
        $$"""
        {"status":"success","pendingPrRequests":[{"branch":"rix/my-fix","baseBranch":"main","title":"My fix","body":"body","bundleFile":"rix_2Fmy-fix.bundle"}],"pendingPushRequests":[{"branch":"rix/my-fix","baseBranch":"main","bundleFile":"rix_2Fmy-fix.bundle"}],"costUsd":0,"durationSeconds":1}
        """;
}
