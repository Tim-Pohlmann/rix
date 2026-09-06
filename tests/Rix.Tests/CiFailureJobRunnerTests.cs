using Rix.Agents;
using Rix.CiFailure;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class CiFailureJobRunnerTests
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
        var ciFailureHost = new StubCiFailureHost(getRun: _ => Task.FromResult(SampleRun("success")));
        var cloneCalled = false;
        var repositoryHost = new StubRepositoryHost(clone: () => { cloneCalled = true; return Task.CompletedTask; });

        var outcome = await CiFailureJobRunner.RunAsync(
            MakeConfig(), ciFailureHost, JobContext(repositoryHost), CancellationToken.None);

        var notRun = AssertNotRun(outcome);
        Assert.IsInstanceOfType<CiFailureSkipped>(notRun.Reason);
        Assert.IsFalse(cloneCalled, "the job pipeline must never run when no failure was detected");
    }

    [TestMethod]
    public async Task RunAsync_ReturnsNotRun_WhenCiFailureCheckErrors()
    {
        var ciFailureHost = new StubCiFailureHost(getRun: _ => throw new HttpRequestException("boom"));

        var outcome = await CiFailureJobRunner.RunAsync(
            MakeConfig(), ciFailureHost, JobContext(new StubRepositoryHost()), CancellationToken.None);

        var notRun = AssertNotRun(outcome);
        Assert.IsInstanceOfType<CiFailureError>(notRun.Reason);
    }

    [TestMethod]
    public async Task RunAsync_RunsJob_WithDetectedPrompt_WhenRunFailed()
    {
        var ciFailureHost = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure")),
            getLogs: _ => Task.FromResult("boom: it broke"),
            findPr: _ => Task.FromResult<int?>(null));

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

        var outcome = await CiFailureJobRunner.RunAsync(
            MakeConfig(), ciFailureHost, JobContext(new StubRepositoryHost(), capture), CancellationToken.None);

        var ran = AssertRan(outcome);
        Assert.IsInstanceOfType<JobSuccess>(ran.Result);
        StringAssert.Contains(capturedPrompt, "CI failed on branch 'rix/fix'");
        StringAssert.Contains(capturedPrompt, "boom: it broke");
    }

    [TestMethod]
    public async Task ExecuteCiFailureJobAsync_Returns0_AndWritesNoResultJson_WhenRunDidNotFail()
    {
        var ciFailureHost = new StubCiFailureHost(getRun: _ => Task.FromResult(SampleRun("success")));

        var exitCode = await Startup.ExecuteCiFailureJobAsync(
            MakeConfig(), CancellationToken.None, ciFailureHost, JobContext(new StubRepositoryHost()));

        Assert.AreEqual(0, exitCode);
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "result.json")));
    }

    [TestMethod]
    public async Task ExecuteCiFailureJobAsync_Returns1_WhenCiFailureCheckErrors()
    {
        var ciFailureHost = new StubCiFailureHost(getRun: _ => throw new HttpRequestException("boom"));

        var exitCode = await Startup.ExecuteCiFailureJobAsync(
            MakeConfig(), CancellationToken.None, ciFailureHost, JobContext(new StubRepositoryHost()));

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public async Task ExecuteCiFailureJobAsync_Returns0_AndWritesResultJson_WhenRunFailedAndJobSucceeded()
    {
        var ciFailureHost = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure")),
            getLogs: _ => Task.FromResult("boom: it broke"),
            findPr: _ => Task.FromResult<int?>(null));

        var exitCode = await Startup.ExecuteCiFailureJobAsync(
            MakeConfig(), CancellationToken.None, ciFailureHost, JobContext(new StubRepositoryHost()));

        Assert.AreEqual(0, exitCode);
        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual("success", doc.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task RunAsync_AllowsPushOnly_ToTheFailingRunsOwnBranch()
    {
        var ciFailureHost = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure", branch: "rix/fix")),
            getLogs: _ => Task.FromResult("boom: it broke"),
            findPr: _ => Task.FromResult<int?>(null));

        var systemPrompt = await CaptureSystemPromptAsync(ciFailureHost);

        Assert.IsNotNull(systemPrompt);
        StringAssert.Contains(systemPrompt, "rix/fix");
    }

    [TestMethod]
    public async Task RunAsync_AllowsNoPush_WhenFailingBranchIsNotARixBranch()
    {
        var ciFailureHost = new StubCiFailureHost(
            getRun: _ => Task.FromResult(SampleRun("failure", branch: "feature/human-work")),
            getLogs: _ => Task.FromResult("boom: it broke"),
            findPr: _ => Task.FromResult<int?>(null));

        var systemPrompt = await CaptureSystemPromptAsync(ciFailureHost);

        Assert.IsNotNull(systemPrompt);
        StringAssert.Contains(systemPrompt, "not allowed any push branches");
    }

    private async Task<string?> CaptureSystemPromptAsync(ICiFailureHost ciFailureHost)
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

        await CiFailureJobRunner.RunAsync(
            MakeConfig(), ciFailureHost, JobContext(new StubRepositoryHost(), capture), CancellationToken.None);

        return systemPrompt;
    }

    private static WorkflowRun SampleRun(string conclusion, string branch = "rix/fix")
    => new(conclusion, "Fix thing", "https://github.com/owner/repo/actions/runs/1", branch);

    private CiFailureJobConfig MakeConfig()
    => TestConfig.ValidCiFailureJob(workDir: _workDir, outputDir: _outputDir);

    private static JobContext JobContext(IRepositoryReadHost host, RunProcessAsync? processRunner = null)
    => new(host, processRunner ?? DefaultRunner, new StubAgent(_ => Task.FromResult<InstallResult>(new Installed())), _ => { }, _ => { });

    private static Task<ProcessResult> DefaultRunner(
        string fileName, IEnumerable<string> args, string workDir,
        IReadOnlyDictionary<string, string>? envOverrides, Action<string>? onLine, CancellationToken ct)
    => fileName switch
    {
        "claude" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
        _ => throw new NotSupportedException($"Unexpected process: {fileName}"),
    };

    private static CiFailureJobNotRun AssertNotRun(CiFailureJobOutcome outcome) => outcome switch
    {
        CiFailureJobNotRun n => n,
        _ => throw new AssertFailedException($"expected CiFailureJobNotRun, got {outcome}"),
    };

    private static CiFailureJobRan AssertRan(CiFailureJobOutcome outcome) => outcome switch
    {
        CiFailureJobRan r => r,
        _ => throw new AssertFailedException($"expected CiFailureJobRan, got {outcome}"),
    };
}
