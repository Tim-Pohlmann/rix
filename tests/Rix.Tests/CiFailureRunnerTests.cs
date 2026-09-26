using Rix.Agents;
using Rix.CiFailure;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class CiFailureRunnerTests
{
    private string _workDir = null!;
    private string _outputDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _workDir = Directory.CreateTempSubdirectory("rix-work-").FullName;
        _outputDir = Directory.CreateTempSubdirectory("rix-out-").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch (DirectoryNotFoundException) { }
        try { Directory.Delete(_outputDir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    [TestMethod]
    public async Task RunAsync_ReturnsNotRun_AndNeverClones_WhenRunDidNotFail()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiSucceeded())));
        var cloneCalled = false;
        var jobHost = new StubGit(clone: () => { cloneCalled = true; return Task.CompletedTask; });

        var outcome = await CiFailureRunner.RunAsync(
            MakeConfig(), Context(ci, jobHost), CancellationToken.None);

        var notRun = AssertNotRun(outcome);
        Assert.IsInstanceOfType<CiFailureSkipped>(notRun.Reason);
        Assert.IsFalse(cloneCalled, "the job pipeline must never run when no failure was detected");
    }

    [TestMethod]
    public async Task RunAsync_ReturnsNotRun_WhenCiFailureCheckErrors()
    {
        var ci = new StubCiHost(getRun: _ => throw new CiHostException("boom"));

        var outcome = await CiFailureRunner.RunAsync(
            MakeConfig(), Context(ci, new StubGit()), CancellationToken.None);

        var notRun = AssertNotRun(outcome);
        Assert.IsInstanceOfType<CiFailureError>(notRun.Reason);
    }

    [TestMethod]
    public async Task RunAsync_RunsJob_WithDetectedPrompt_WhenRunFailed()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())),
            getLogs: _ => Task.FromResult("boom: it broke"));

        string? capturedPrompt = null;
        RunProcessAsync capture = (fileName, args, workDir, envOverrides, onLine, ct) =>
        {
            if (fileName == "claude")
            {
                var argList = args.ToList();
                // The task prompt is the positional arg immediately before --append-system-prompt
                // (see ClaudeAgent.BuildInvocation), not the appended system prompt itself.
                var idx = argList.IndexOf("--append-system-prompt");
                if (idx >= 1)
                    capturedPrompt = argList[idx - 1];
            }
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        var outcome = await CiFailureRunner.RunAsync(
            MakeConfig(), Context(ci, new StubGit(), capture), CancellationToken.None);

        var ran = AssertRan(outcome);
        Assert.IsInstanceOfType<JobSuccess>(ran.Result);
        StringAssert.Contains(capturedPrompt, "CI failed on branch 'rix/fix'");
        StringAssert.Contains(capturedPrompt, "boom: it broke");
        StringAssert.Contains(ran.Job.Agent.Prompt, "CI failed on branch 'rix/fix'");
    }

    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns0_AndWritesNoResultJson_WhenRunDidNotFail()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiSucceeded())));

        // The job half is stubbed but never reached: the run didn't fail, so no agent runs.
        var exitCode = await Startup.ExecuteCiFailureAsync(
            MakeConfig(), CancellationToken.None, Context(ci, new StubGit()));

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "result.json")));
    }

    [TestMethod]
    public async Task RunAsync_ReturnsNotRun_AndNeverClones_WhenRixCommitsAlreadyFillTheBranchTip()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())));
        var repoHost = new StubCiFailureRepoHost(countRixCommits: _ => Task.FromResult(CiFailureConfig.DefaultMaxRixCommits));
        var cloneCalled = false;
        var jobHost = new StubGit(clone: () => { cloneCalled = true; return Task.CompletedTask; });

        var outcome = await CiFailureRunner.RunAsync(
            MakeConfig(), Context(ci, jobHost, repoHost: repoHost), CancellationToken.None);

        var notRun = AssertNotRun(outcome);
        Assert.IsInstanceOfType<CiFailureLoopGuarded>(notRun.Reason);
        Assert.IsFalse(cloneCalled, "a guarded failure must not start an agent run at all");
    }

    [TestMethod]
    public async Task RunAsync_ReturnsNotRun_AndNeverClones_WhenTheFailureComesFromAFork()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), headRepo: "outsider/repo")));
        var cloneCalled = false;
        var jobHost = new StubGit(clone: () => { cloneCalled = true; return Task.CompletedTask; });

        var outcome = await CiFailureRunner.RunAsync(
            MakeConfig(), Context(ci, jobHost), CancellationToken.None);

        var notRun = AssertNotRun(outcome);
        Assert.IsInstanceOfType<CiFailureUntrustedRun>(notRun.Reason);
        Assert.IsFalse(cloneCalled, "a fork's failure must not start an agent run at all");
    }

    /// <summary>Exit 0, like every other reason not to act: a fork PR failing CI is the normal
    /// course of events, not a broken rix run for the repo's Actions tab to go red over.</summary>
    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns0_AndWritesNoResultJson_WhenTheFailureComesFromAFork()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), headRepo: "outsider/repo")));

        var exitCode = await Startup.ExecuteCiFailureAsync(
            MakeConfig(), CancellationToken.None, Context(ci, new StubGit()));

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "result.json")));
    }

    [TestMethod]
    public async Task RunAsync_PassesTheConfiguredCap_ToTheLoopGuard()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())),
            getLogs: _ => Task.FromResult("boom"));
        var repoHost = new StubCiFailureRepoHost(countRixCommits: _ => Task.FromResult(1));

        await CiFailureRunner.RunAsync(
            TestConfig.ValidCiFailure(workDir: _workDir, outputDir: _outputDir, maxRixCommits: 2),
            Context(ci, new StubGit(), repoHost: repoHost), CancellationToken.None);

        Assert.AreEqual(2, repoHost.MaxRixCommits?.Value);
    }

    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns0_AndWritesNoResultJson_WhenGuardedAgainstALoop()
    {
        var ci = new StubCiHost(getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())));
        var repoHost = new StubCiFailureRepoHost(countRixCommits: _ => Task.FromResult(CiFailureConfig.DefaultMaxRixCommits));

        // Exit 0, like any other reason not to act: the branch being rix's own work is a decision,
        // not a failure of this run, and a non-zero exit would fail the caller's workflow for it.
        var exitCode = await Startup.ExecuteCiFailureAsync(
            MakeConfig(), CancellationToken.None, Context(ci, new StubGit(), repoHost: repoHost));

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "result.json")));
    }

    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns1_WhenCiFailureCheckErrors()
    {
        var ci = new StubCiHost(getRun: _ => throw new CiHostException("boom"));

        // The check errors before the job path the stubbed job half would serve ever runs.
        var exitCode = await Startup.ExecuteCiFailureAsync(
            MakeConfig(), CancellationToken.None, Context(ci, new StubGit()));

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task ExecuteCiFailureAsync_Returns0_AndWritesResultJson_WhenRunFailedAndJobSucceeded()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed())),
            getLogs: _ => Task.FromResult("boom: it broke"));

        var exitCode = await Startup.ExecuteCiFailureAsync(
            MakeConfig(), CancellationToken.None, Context(ci, new StubGit()));

        Assert.AreEqual(0, exitCode);
        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual("success", doc.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task RunAsync_AllowsPushOnly_ToTheFailingRunsOwnBranch()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), branch: "rix/fix")),
            getLogs: _ => Task.FromResult("boom: it broke"));

        var systemPrompt = await CaptureSystemPromptAsync(ci);

        Assert.IsNotNull(systemPrompt);
        StringAssert.Contains(systemPrompt, "rix/fix");
    }

    [TestMethod]
    public async Task RunAsync_AllowsPush_ToTheFailingRunsOwnBranch_EvenWhenNotARixBranch()
    {
        var ci = new StubCiHost(
            getRun: _ => Task.FromResult(TestRuns.Sample(new CiFailed(), branch: "feature/human-work")),
            getLogs: _ => Task.FromResult("boom: it broke"));

        var systemPrompt = await CaptureSystemPromptAsync(ci);

        Assert.IsNotNull(systemPrompt);
        Assert.IsFalse(systemPrompt.Contains("not allowed any push branches"));
        StringAssert.Contains(systemPrompt, "feature/human-work");
    }

    private async Task<string?> CaptureSystemPromptAsync(ICiHost ci)
    {
        string? systemPrompt = null;
        RunProcessAsync capture = (fileName, args, workDir, envOverrides, onLine, ct) =>
        {
            if (fileName == "claude")
            {
                var argList = args.ToList();
                var idx = argList.IndexOf("--append-system-prompt");
                if (idx >= 0 && idx + 1 < argList.Count)
                    systemPrompt = argList[idx + 1];
            }
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await CiFailureRunner.RunAsync(
            MakeConfig(), Context(ci, new StubGit(), capture), CancellationToken.None);

        return systemPrompt;
    }

    private CiFailureConfig MakeConfig()
    => TestConfig.ValidCiFailure(workDir: _workDir, outputDir: _outputDir);

    /// <summary>Every half stubbed: the CI host the check reads the run from, the repo host it
    /// judges that run against, and the job context the agent run would get, with every
    /// collaborator <see cref="CiFailureRunner"/> wires up replaced. The repo host is optional
    /// because most of these tests decide on the run alone, and an unstubbed one answers "no open
    /// PR, no rix commits".</summary>
    private static CiFailureContext Context(
        ICiHost ci, IGit git, RunProcessAsync? processRunner = null, ICiFailureRepoHost? repoHost = null)
    => new(ci, repoHost ?? new StubCiFailureRepoHost(), JobContext(git, processRunner));

    private static JobContext JobContext(IGit git, RunProcessAsync? processRunner = null)
    => new(git, processRunner ?? DefaultRunner, new StubAgent(_ => Task.FromResult<InstallResult>(new Installed())), _ => { }, _ => { },
        new StubAgentHomeFetcher());

    private static Task<ProcessResult> DefaultRunner(
        string fileName, IEnumerable<string> args, string workDir,
        IReadOnlyDictionary<string, string>? envOverrides, Action<string>? onLine, CancellationToken ct)
    => fileName switch
    {
        "claude" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
        _ => throw new NotSupportedException($"Unexpected process: {fileName}"),
    };

    private static CiFailureNotRun AssertNotRun(CiFailureOutcome outcome) => outcome switch
    {
        CiFailureNotRun n => n,
        _ => throw new AssertFailedException($"expected CiFailureNotRun, got {outcome}"),
    };

    private static CiFailureRan AssertRan(CiFailureOutcome outcome) => outcome switch
    {
        CiFailureRan r => r,
        _ => throw new AssertFailedException($"expected CiFailureRan, got {outcome}"),
    };
}
