using Rix.Agents;
using Rix.Job;
using Rix.Process;
using Rix.Repository;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Rix.Tests;

[TestClass]
public class JobRunnerTests
{
    private static readonly HttpClient HttpClient = new();

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
    public async Task RunAsync_Returns1_WhenClaudeFails()
    {
        var result = await Run(claudeExitCode: 1);

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public async Task RunAsync_Returns1_WhenClaudeTimesOut()
    {
        var result = await Run(claudeTimedOut: true);

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public async Task RunAsync_Returns0_WhenClaudeSucceeds()
    {
        var result = await Run();

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task RunAsync_Returns2_WhenClaudeInstallerFails()
    {
        var result = await Startup.ExecuteJobAsync(
            MakeConfig(), CancellationToken.None,
            Context(new StubRepositoryHost(), FakeRunner(),
                _ => Task.FromResult<InstallResult>(new InstallFailed("install failed"))));

        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public async Task RunAsync_DoesNotClone_WhenClaudeInstallerFails()
    {
        var host = new TrackingRepositoryHost();

        await JobRunner.RunAsync(MakeConfig(),
            Context(host, FakeRunner(), _ => Task.FromResult<InstallResult>(new InstallFailed("install failed"))),
            CancellationToken.None);

        Assert.IsFalse(host.CloneCalled);
    }

    [TestMethod]
    public async Task RunAsync_WritesResultJson_OnSuccess()
    {
        await Run();

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "result.json")));
    }

    [TestMethod]
    public async Task RunAsync_WritesResultJson_OnFailure()
    {
        await Run(claudeExitCode: 1);

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("failure", doc.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task RunAsync_WritesResultJson_OnSetupFailure()
    {
        await Startup.ExecuteJobAsync(
            MakeConfig(), CancellationToken.None,
            Context(new StubRepositoryHost(), FakeRunner(),
                _ => Task.FromResult<InstallResult>(new InstallFailed("install failed"))));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.AreEqual("setupFailure", doc.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task ExecuteJobAsync_StillReturnsExitCode_WhenResultJsonCannotBeWritten()
    {
        // Forces the write to fail with UnauthorizedAccessException: "result.json" already exists
        // as a directory at that path, so it can't be opened as a file.
        Directory.CreateDirectory(Path.Combine(_outputDir, "result.json"));

        var result = await Run();

        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task ExecuteJobAsync_StillWritesResultJson_WhenStdoutIsABrokenPipe()
    => await AssertSurvivesStdoutFailure(new IOException("broken pipe"));

    [TestMethod]
    public async Task ExecuteJobAsync_StillWritesResultJson_WhenStdoutIsAlreadyDisposed()
    => await AssertSurvivesStdoutFailure(new ObjectDisposedException(nameof(TextWriter)));

    private async Task AssertSurvivesStdoutFailure(Exception writeException)
    {
        var original = Console.Out;
        Console.SetOut(new ThrowingWriter(writeException));
        int result;
        try { result = await Run(); }
        finally { Console.SetOut(original); }

        Assert.AreEqual(0, result);
        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "result.json")));
    }

    /// <summary>Simulates a broken/closed stdout: every line write fails with
    /// <paramref name="exception"/>, e.g. <see cref="IOException"/> for a broken pipe or
    /// <see cref="ObjectDisposedException"/> for an already-disposed stream. Overrides the
    /// synchronous <see cref="WriteLine(string?)"/> rather than <c>WriteLineAsync</c> -
    /// <see cref="Console.SetOut"/> wraps the writer in a synchronized <see cref="TextWriter"/>
    /// whose async methods delegate to the synchronous ones under a lock, so an override of the
    /// async method alone is never reached.</summary>
    private sealed class ThrowingWriter(Exception exception) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) => throw exception;
    }

    [TestMethod]
    public async Task RunAsync_ResultJson_ContainsPendingPrRequests()
    {
        await Run(pr: new("rix/my-fix", "main", "My fix", "body"));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(1, doc.RootElement.GetProperty("pendingPrRequests").GetArrayLength());
    }

    [TestMethod]
    public async Task RunAsync_ResultJson_ContainsBundleFileName()
    {
        await Run(pr: new("rix/my-fix", "main", "My fix", "body"));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        StringAssert.Contains(json, "rix_2Fmy-fix.bundle");
    }

    [TestMethod]
    public async Task RunAsync_CreatesBundleFile()
    {
        await Run(pr: new("rix/my-fix", "main", "My fix", "body"));

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "rix_2Fmy-fix.bundle")));
    }

    [TestMethod]
    public async Task RunAsync_EncodesSlashesInBundleFileName()
    {
        await Run(pr: new("rix/feat/sub", "main", "T", "b"));

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "rix_2Ffeat_2Fsub.bundle")));
    }

    [TestMethod]
    public async Task RunAsync_SkipsDuplicateQueuedBranch()
    {
        // LocalApiServer forwards log lines from threadpool threads while the main thread writes the
        // dedup message, so the sink must tolerate concurrent writes.
        var log = new ConcurrentQueue<string>();

        RunProcessAsync runner = async (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                var apiUrl = ExtractApiUrlFromSystemPrompt(a);
                for (var i = 0; i < 2; i++)
                {
                    using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
                    {
                        branch = "rix/dup", baseBranch = "main", title = "T", body = "b",
                    }, ct);
                    response.EnsureSuccessStatusCode();
                }
            }
            return new ProcessSuccess();
        };

        var result = await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), runner,
                _ => Task.FromResult<InstallResult>(new Installed()), logLine: log.Enqueue),
            CancellationToken.None);

        var success = (JobSuccess)result;
        Assert.AreEqual(1, success.PendingPrRequests.Count);
        Assert.AreEqual(1, Directory.GetFiles(_outputDir, "*.bundle").Length);
        Assert.IsTrue(log.Any(l => l.Contains("duplicate")));
    }

    [TestMethod]
    public async Task RunAsync_ClonesRepo()
    {
        var host = new TrackingRepositoryHost();

        await JobRunner.RunAsync(MakeConfig(),
            Context(host, FakeRunner(), _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsTrue(host.CloneCalled);
    }

    [TestMethod]
    public async Task RunAsync_ConfiguresGitIdentity_InCloneDir()
    {
        var host = new TrackingRepositoryHost();
        string? agentWorkingDir = null;
        RunProcessAsync tracker = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") agentWorkingDir = d;
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await JobRunner.RunAsync(MakeConfig(),
            Context(host, tracker, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsTrue(host.ConfigureGitCalled, "the clone must be left commit-ready for the agent");
        Assert.AreEqual(agentWorkingDir, host.ConfigureGitDirectory,
            "git identity must be configured in the same directory the agent runs in");
    }

    [TestMethod]
    public async Task RunAsync_ReturnsSetupFailure_WhenGitConfigFails()
    {
        var host = new StubRepositoryHost(
            configureGit: () => throw new InvalidOperationException("git config failed: exited with code 128"));

        var result = await JobRunner.RunAsync(MakeConfig(),
            Context(host, FakeRunner(), _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsInstanceOfType<SetupFailure>(result);
    }

    [TestMethod]
    public async Task RunAsync_CleansUpCloneDir()
    {
        string? capturedCloneDir = null;

        RunProcessAsync tracker = async (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") capturedCloneDir = d;
            return new ProcessSuccess();
        };

        await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), tracker, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsNotNull(capturedCloneDir);
        Assert.IsFalse(Directory.Exists(capturedCloneDir), "Clone dir should be deleted after run");
    }

    [TestMethod]
    public async Task RunAsync_CleansUpCloneDir_EvenOnClaudeFailure()
    {
        string? capturedCloneDir = null;

        RunProcessAsync tracker = (f, a, d, e, onLine, ct) =>
        {
            capturedCloneDir = d;
            return Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1"));
        };

        await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), tracker, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsNotNull(capturedCloneDir);
        Assert.IsFalse(Directory.Exists(capturedCloneDir));
    }

    [TestMethod]
    public async Task RunAsync_PassesNonNullOnStdoutLine_ToClaudeProcess()
    {
        Action<string>? claudeCallback = null;

        RunProcessAsync capture = async (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                claudeCallback = onLine;
                var apiUrl = ExtractApiUrlFromSystemPrompt(a);
                using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
                {
                    branch = "rix/test", baseBranch = "main", title = "T", body = "b",
                }, ct);
                response.EnsureSuccessStatusCode();
            }
            return new ProcessSuccess();
        };

        await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), capture, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsNotNull(claudeCallback);
    }

    [TestMethod]
    public async Task RunAsync_ForwardsClaudeOutputLines_ToLogSink()
    {
        var log = new List<string>();

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") onLine?.Invoke("test line");
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), runner,
                _ => Task.FromResult<InstallResult>(new Installed()),
                logLine: log.Add),
            CancellationToken.None);

        CollectionAssert.Contains(log, "test line");
    }

    [TestMethod]
    public async Task RunAsync_PassesApiUrlInSystemPromptArg()
    {
        var systemPrompt = await CaptureSystemPromptAsync(MakeConfig());

        Assert.IsNotNull(systemPrompt);
        StringAssert.Contains(systemPrompt, "A local API is available at http://");
        StringAssert.Contains(systemPrompt, "/pr");
    }

    [TestMethod]
    public async Task RunAsync_ExtractsCostUsd_FromClaudeResultOutput()
    {
        const string resultLine = """{"type":"result","subtype":"success","total_cost_usd":0.028521,"usage":{"input_tokens":2036,"output_tokens":14}}""";

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
            Task.FromResult<ProcessResult>(new ProcessSuccess((f == "claude") switch { true => resultLine, false => null }));

        await Startup.ExecuteJobAsync(MakeConfig(), CancellationToken.None,
            Context(new StubRepositoryHost(), runner,
                _ => Task.FromResult<InstallResult>(new Installed())));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0.028521m, doc.RootElement.GetProperty("costUsd").GetDecimal());
    }

    [TestMethod]
    public async Task RunAsync_CostIsZero_WhenClaudeOutputIsNotAResultLine()
    {
        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
            Task.FromResult<ProcessResult>(new ProcessSuccess((f == "claude") switch { true => "thinking...", false => null }));

        await Startup.ExecuteJobAsync(MakeConfig(), CancellationToken.None,
            Context(new StubRepositoryHost(), runner,
                _ => Task.FromResult<InstallResult>(new Installed())));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0m, doc.RootElement.GetProperty("costUsd").GetDecimal());
    }

    [TestMethod]
    public async Task RunAsync_Returns1_WhenGitBundleFails()
    {
        RunProcessAsync runner = async (f, a, d, e, onLine, ct) =>
        {
            var apiUrl = ExtractApiUrlFromSystemPrompt(a);
            using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
            {
                branch = "rix/test", baseBranch = "main", title = "T", body = "b",
            }, ct);
            response.EnsureSuccessStatusCode();
            return new ProcessSuccess();
        };

        var hostWithFailingBundle = new StubRepositoryHost(
            createBundle: _ => throw new InvalidOperationException("git bundle failed: exited with code 128"));

        var result = await Startup.ExecuteJobAsync(MakeConfig(), CancellationToken.None,
            Context(hostWithFailingBundle, runner,
                _ => Task.FromResult<InstallResult>(new Installed())));

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public async Task RunAsync_TreatsNonNumericCost_AsZero()
    {
        const string resultLine = """{"type":"result","total_cost_usd":"not-a-number"}""";

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
            Task.FromResult<ProcessResult>(new ProcessSuccess((f == "claude") switch { true => resultLine, false => null }));

        await Startup.ExecuteJobAsync(MakeConfig(), CancellationToken.None,
            Context(new StubRepositoryHost(), runner,
                _ => Task.FromResult<InstallResult>(new Installed())));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0m, doc.RootElement.GetProperty("costUsd").GetDecimal());
    }

    [TestMethod]
    public async Task RunAsync_ResultJson_ContainsPendingPushRequests()
    {
        var host = new StubRepositoryHost(branchExists: _ => Task.FromResult(true));
        RunProcessAsync runner = async (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                var apiUrl = ExtractApiUrlFromSystemPrompt(a);
                using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/push"), new
                {
                    branch = "rix/my-fix", baseBranch = "main",
                }, ct);
                response.EnsureSuccessStatusCode();
            }
            return new ProcessSuccess();
        };

        await Startup.ExecuteJobAsync(MakeConfig(allowedPushBranches: "rix/my-fix"), CancellationToken.None,
            Context(host, runner, _ => Task.FromResult<InstallResult>(new Installed())));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(1, doc.RootElement.GetProperty("pendingPushRequests").GetArrayLength());
        Assert.AreEqual("rix/my-fix", doc.RootElement.GetProperty("pendingPushRequests")[0].GetProperty("branch").GetString());
    }

    [TestMethod]
    public async Task RunAsync_CreatesBundleFile_ForPush()
    {
        var host = new StubRepositoryHost(branchExists: _ => Task.FromResult(true));
        RunProcessAsync runner = async (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                var apiUrl = ExtractApiUrlFromSystemPrompt(a);
                using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/push"), new
                {
                    branch = "rix/my-fix", baseBranch = "main",
                }, ct);
                response.EnsureSuccessStatusCode();
            }
            return new ProcessSuccess();
        };

        await JobRunner.RunAsync(MakeConfig(allowedPushBranches: "rix/my-fix"),
            Context(host, runner, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "rix_2Fmy-fix.bundle")));
    }

    [TestMethod]
    public async Task RunAsync_SkipsDuplicateQueuedPush()
    {
        var log = new ConcurrentQueue<string>();
        var host = new StubRepositoryHost(branchExists: _ => Task.FromResult(true));
        RunProcessAsync runner = async (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                var apiUrl = ExtractApiUrlFromSystemPrompt(a);
                for (var i = 0; i < 2; i++)
                {
                    using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/push"), new
                    {
                        branch = "rix/dup", baseBranch = "main",
                    }, ct);
                    response.EnsureSuccessStatusCode();
                }
            }
            return new ProcessSuccess();
        };

        var result = await JobRunner.RunAsync(MakeConfig(allowedPushBranches: "rix/dup"),
            Context(host, runner, _ => Task.FromResult<InstallResult>(new Installed()), logLine: log.Enqueue),
            CancellationToken.None);

        var success = (JobSuccess)result;
        Assert.AreEqual(1, success.PendingPushRequests.Count);
        Assert.AreEqual(1, Directory.GetFiles(_outputDir, "*.bundle").Length);
        Assert.IsTrue(log.Any(l => l.Contains("duplicate")));
    }

    [TestMethod]
    public async Task RunAsync_SystemPrompt_ListsAllowedPushBranches_WhenRestricted()
    {
        var systemPrompt = await CaptureSystemPromptAsync(MakeConfig(allowedPushBranches: "rix/continue-a,rix/continue-b"));

        Assert.IsNotNull(systemPrompt);
        StringAssert.Contains(systemPrompt, "rix/continue-a");
        StringAssert.Contains(systemPrompt, "rix/continue-b");
    }

    [TestMethod]
    public async Task RunAsync_SystemPrompt_ExplainsPushIsDisabled_ByDefault()
    {
        var systemPrompt = await CaptureSystemPromptAsync(MakeConfig());

        Assert.IsNotNull(systemPrompt);
        Assert.IsFalse(systemPrompt.Contains("may only push onto the branches"), "a default job has no allow-list to name");
        StringAssert.Contains(systemPrompt, "not allowed any push branches");
    }

    [TestMethod]
    public async Task RunAsync_RejectsPush_ToDisallowedBranch()
    {
        var host = new StubRepositoryHost(branchExists: _ => Task.FromResult(true));
        RunProcessAsync runner = async (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                var apiUrl = ExtractApiUrlFromSystemPrompt(a);
                using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/push"), new
                {
                    branch = "rix/not-allowed", baseBranch = "main",
                }, ct);
                if (response.StatusCode != HttpStatusCode.Forbidden)
                    throw new InvalidOperationException($"expected 403 for disallowed push, got {response.StatusCode}");
            }
            return new ProcessSuccess();
        };

        await Startup.ExecuteJobAsync(MakeConfig(allowedPushBranches: "rix/allowed"),
            CancellationToken.None,
            Context(host, runner, _ => Task.FromResult<InstallResult>(new Installed())));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0, doc.RootElement.GetProperty("pendingPushRequests").GetArrayLength());
    }

    [TestMethod]
    public async Task RunAsync_SystemPrompt_MentionsPushEndpoint()
    {
        var systemPrompt = await CaptureSystemPromptAsync(MakeConfig());

        Assert.IsNotNull(systemPrompt);
        StringAssert.Contains(systemPrompt, "/push");
    }

    [TestMethod]
    public async Task RunAsync_ReturnsJobSuccess_WithoutWritingResultJson()
    {
        var result = await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), FakeRunner(), _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsInstanceOfType<JobSuccess>(result);
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "result.json")),
            "JobRunner core must not perform the result.json write — that is the shell's job");
    }

    [TestMethod]
    public async Task RunAsync_JobFailureError_IncludesAgentDiagnostic_WhenPresent()
    {
        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
            Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1", Diagnostic: "Invalid API key"));

        var result = await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), runner, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        var failure = (JobFailure)result;
        Assert.AreEqual("agent failed: exited with code 1: Invalid API key", failure.Error);
    }

    [TestMethod]
    public async Task RunAsync_JobFailureError_OmitsDiagnosticSuffix_WhenAbsent()
    {
        var result = await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), FakeRunner(claudeExitCode: 1), _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        var failure = (JobFailure)result;
        Assert.AreEqual("agent failed: exited with code 1", failure.Error);
    }

    [TestMethod]
    public async Task RunAsync_ReturnsSetupFailure_WhenInstallerFails()
    {
        var result = await JobRunner.RunAsync(MakeConfig(),
            Context(new StubRepositoryHost(), FakeRunner(), _ => Task.FromResult<InstallResult>(new InstallFailed("nope"))),
            CancellationToken.None);

        Assert.IsInstanceOfType<SetupFailure>(result);
    }

    [TestMethod]
    public async Task RunAsync_ReturnsSetupFailure_WhenCloneFails()
    {
        var host = new StubRepositoryHost(clone: () => throw new InvalidOperationException("git clone failed: exit code 128"));

        var result = await JobRunner.RunAsync(MakeConfig(),
            Context(host, FakeRunner(), _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsInstanceOfType<SetupFailure>(result);
    }

    // ---- helpers ----

    private static JobContext Context(
        IRepositoryReadHost host,
        RunProcessAsync processRunner,
        Func<CancellationToken, Task<InstallResult>> install,
        LogLine? logLine = null)
    => new(host, processRunner, new StubAgent(install), logLine ?? (_ => { }));

    private Task<int> Run(int claudeExitCode = 0, bool claudeTimedOut = false, QueuedPrSpec? pr = null)
    => Startup.ExecuteJobAsync(MakeConfig(), CancellationToken.None,
        Context(new StubRepositoryHost(),
            FakeRunner(claudeExitCode, claudeTimedOut, pr),
            _ => Task.FromResult<InstallResult>(new Installed())));

    private JobConfig MakeConfig(string? allowedPushBranches = null)
    => TestConfig.Valid(
        prompt: "Do something", workDir: _workDir, outputDir: _outputDir,
        allowedPushBranches: allowedPushBranches);

    private static RunProcessAsync FakeRunner(
        int claudeExitCode = 0,
        bool claudeTimedOut = false,
        QueuedPrSpec? pr = null)
    => async (fileName, args, workDir, envOverrides, onLine, ct) => fileName switch
    {
        "claude" => await SimulateClaudeAsync(claudeExitCode, claudeTimedOut, pr, args, ct),
        _ => throw new NotSupportedException($"Unexpected process: {fileName}"),
    };

    /// <summary>Runs a job with a no-op agent and returns the system prompt claude was invoked with,
    /// for tests that assert on prompt content rather than agent behavior.</summary>
    private static async Task<string?> CaptureSystemPromptAsync(JobConfig config)
    {
        string? systemPrompt = null;
        RunProcessAsync capture = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                var argList = a.ToList();
                var idx = argList.IndexOf("--append-system-prompt");
                if (idx >= 0 && idx + 1 < argList.Count)
                    systemPrompt = argList[idx + 1];
            }
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await JobRunner.RunAsync(config,
            Context(new StubRepositoryHost(), capture, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        return systemPrompt;
    }

    private static string ExtractApiUrlFromSystemPrompt(IEnumerable<string> args)
    {
        var argList = args.ToList();
        var idx = argList.IndexOf("--append-system-prompt");
        if (idx < 0 || idx + 1 >= argList.Count)
            throw new InvalidOperationException("Expected --append-system-prompt arg not found.");
        const string marker = "A local API is available at ";
        var prompt = argList[idx + 1];
        var start = prompt.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException($"Expected marker '{marker}' not found in system prompt.");
        start += marker.Length;
        var end = prompt.IndexOfAny(['\n', '\r'], start);
        return ((end < 0) switch { true => prompt[start..], false => prompt[start..end] }).Trim();
    }

    private static async Task<ProcessResult> SimulateClaudeAsync(
        int exitCode, bool timedOut, QueuedPrSpec? pr, IEnumerable<string> args, CancellationToken cancellationToken)
    {
        if (pr is not null)
        {
            var apiUrl = ExtractApiUrlFromSystemPrompt(args);
            using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
            {
                branch = pr.Branch,
                title = pr.Title,
                body = pr.Body,
                baseBranch = pr.BaseBranch,
            }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        if (timedOut) return new ProcessFailure("timed out");
        return (exitCode == 0) switch
        {
            true => new ProcessSuccess(),
            false => (ProcessResult)new ProcessFailure($"exited with code {exitCode}"),
        };
    }

    private record QueuedPrSpec(string Branch, string BaseBranch, string Title, string Body);

    private sealed class TrackingRepositoryHost : IRepositoryReadHost
    {
        public bool CloneCalled { get; private set; }
        public bool ConfigureGitCalled { get; private set; }
        public string? ConfigureGitDirectory { get; private set; }
        public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
        {
            CloneCalled = true;
            return Task.CompletedTask;
        }
        public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
        => Task.FromResult(false);
        public Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
        => Task.FromResult(true);
        public Task ConfigureGitAsync(string repoDirectory, CancellationToken cancellationToken)
        {
            ConfigureGitCalled = true;
            ConfigureGitDirectory = repoDirectory;
            return Task.CompletedTask;
        }
        public Task CreateBundleAsync(
            string repoDirectory,
            string bundlePath,
            BranchName baseBranch,
            BranchName branch,
            CancellationToken cancellationToken)
        => Task.CompletedTask;
    }
}
