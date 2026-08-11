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
    public async Task RunAsync_OpensStackedPrs_InDependencyOrder_RegardlessOfQueueOrder()
    {
        // rix/stack-2 is queued first but its base branch is rix/stack-1 (also queued) — GitHub
        // requires the base branch to already exist on the remote before a PR can be opened onto
        // it, so SubmitRunner must open rix/stack-1 first even though it was queued second.
        WriteResultJson(TwoStackedPrsJson());
        File.WriteAllText(Path.Combine(_inputDir, "stack-1.bundle"), "fake-bundle");
        File.WriteAllText(Path.Combine(_inputDir, "stack-2.bundle"), "fake-bundle");
        var host = new StubSubmitHost();

        var result = await Run(host);

        AssertSuccess(result);
        CollectionAssert.AreEqual
        (
            new[] { "rix/stack-1", "rix/stack-2" },
            host.CreatedPrs.Select(pr => pr.Branch.Value).ToArray()
        );
    }

    [TestMethod]
    public async Task RunAsync_Fails_WhenQueuedPrsHaveCyclicBaseBranches()
    {
        WriteResultJson(CyclicPrsJson());
        var host = new StubSubmitHost();

        var result = await Run(host);

        AssertFailure(result, "cyclic base-branch dependency");
        Assert.AreEqual(0, host.CreatedPrs.Count);
        Assert.IsFalse(host.CloneCalled, "must not clone when the queue can't be ordered");
    }

    // ---- helpers ----

    private Task<ISubmitResult> Run(StubSubmitHost host, RunProcessAsync? runner = null)
    => SubmitRunner.RunAsync
    (
        TestConfig.ValidSubmit(inputDir: _inputDir, workDir: _workDir),
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

    private static string TwoStackedPrsJson() =>
        $$"""
        {"status":"success","pendingPrRequests":[
          {"branch":"rix/stack-2","baseBranch":"rix/stack-1","title":"t","body":"b","bundleFile":"stack-2.bundle"},
          {"branch":"rix/stack-1","baseBranch":"main","title":"t","body":"b","bundleFile":"stack-1.bundle"}
        ],"costUsd":0,"durationSeconds":1}
        """;

    private static string CyclicPrsJson() =>
        $$"""
        {"status":"success","pendingPrRequests":[
          {"branch":"rix/a","baseBranch":"rix/b","title":"t","body":"b","bundleFile":"a.bundle"},
          {"branch":"rix/b","baseBranch":"rix/a","title":"t","body":"b","bundleFile":"b.bundle"}
        ],"costUsd":0,"durationSeconds":1}
        """;
}
