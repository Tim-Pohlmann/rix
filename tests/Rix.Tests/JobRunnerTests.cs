using System.Net.Http.Json;
using System.Text.Json;
using Rix.Claude;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

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
        var result = await Startup.RunJobAsync(
            MakeConfig(),
            processRunner: FakeRunner(),
            claudeInstaller: _ => Task.FromResult<InstallResult>(new InstallFailed("install failed")));

        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public async Task RunAsync_DoesNotClone_WhenClaudeInstallerFails()
    {
        var host = new TrackingRepositoryHost();

        await JobRunner.RunAsync(MakeConfig(),
            Effects(host, FakeRunner(), _ => Task.FromResult<InstallResult>(new InstallFailed("install failed"))),
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
    public async Task RunAsync_DoesNotWriteResultJson_OnFailure()
    {
        await Run(claudeExitCode: 1);

        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "result.json")));
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
    public async Task RunAsync_ClonesRepo()
    {
        var host = new TrackingRepositoryHost();

        await JobRunner.RunAsync(MakeConfig(),
            Effects(host, FakeRunner(), _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsTrue(host.CloneCalled);
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
            Effects(new StubRepositoryHost(), tracker, _ => Task.FromResult<InstallResult>(new Installed())),
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
            Effects(new StubRepositoryHost(), tracker, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsNotNull(capturedCloneDir);
        Assert.IsFalse(Directory.Exists(capturedCloneDir));
    }

    [TestMethod]
    public async Task RunAsync_PassesNonNullOnStdoutLine_ToClaudeProcess()
    {
        Action<string>? claudeCallback = null;
        Action<string>? gitCallback = _ => { }; // non-null sentinel; nulled out when git runs with null callback

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
            else if (f == "git") gitCallback = onLine;
            return new ProcessSuccess();
        };

        await JobRunner.RunAsync(MakeConfig(),
            Effects(new StubRepositoryHost(), capture, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsNotNull(claudeCallback);
        Assert.IsNull(gitCallback);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task RunAsync_ForwardsClaudeOutputLines_ToStderr()
    {
        var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);

        try
        {
            RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
            {
                if (f == "claude") onLine?.Invoke("test line");
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            };

            await Startup.RunJobAsync(MakeConfig(),
                host: new StubRepositoryHost(), processRunner: runner,
                claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

            StringAssert.Contains(stderr.ToString(), "test line");
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [TestMethod]
    public async Task RunAsync_PassesApiUrlInSystemPromptArg()
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

        await JobRunner.RunAsync(MakeConfig(),
            Effects(new StubRepositoryHost(), capture, _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.IsNotNull(systemPrompt);
        StringAssert.Contains(systemPrompt, "A local API is available at http://");
        StringAssert.Contains(systemPrompt, "/pr");
    }

    [TestMethod]
    public async Task RunAsync_ExtractsCostUsd_FromClaudeResultLine()
    {
        const string resultLine = """{"type":"result","subtype":"success","total_cost_usd":0.028521,"usage":{"input_tokens":2036,"output_tokens":14}}""";

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") onLine?.Invoke(resultLine);
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await Startup.RunJobAsync(MakeConfig(),
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0.028521m, doc.RootElement.GetProperty("costUsd").GetDecimal());
    }

    [TestMethod]
    public async Task RunAsync_UsesLastResultLine_ForCostUsd()
    {
        const string line1 = """{"type":"result","total_cost_usd":0.01}""";
        const string line2 = """{"type":"result","total_cost_usd":0.05}""";

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") { onLine?.Invoke(line1); onLine?.Invoke(line2); }
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await Startup.RunJobAsync(MakeConfig(),
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0.05m, doc.RootElement.GetProperty("costUsd").GetDecimal());
    }

    [TestMethod]
    public async Task RunAsync_IgnoresNonResultJsonLines()
    {
        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                onLine?.Invoke("not json at all");
                onLine?.Invoke("""{"type":"assistant","message":"hello"}""");
                onLine?.Invoke("{invalid json}");
                onLine?.Invoke("""{"type":123}""");
                onLine?.Invoke("[]");
                onLine?.Invoke("null");
                onLine?.Invoke("42");
            }
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await Startup.RunJobAsync(MakeConfig(),
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0m, doc.RootElement.GetProperty("costUsd").GetDecimal());
    }

    [TestMethod]
    public async Task RunAsync_Returns1_WhenGitBundleFails()
    {
        RunProcessAsync runner = async (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude")
            {
                var apiUrl = ExtractApiUrlFromSystemPrompt(a);
                using var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
                {
                    branch = "rix/test", baseBranch = "main", title = "T", body = "b",
                }, ct);
                response.EnsureSuccessStatusCode();
                return new ProcessSuccess();
            }
            return new ProcessFailure("exited with code 1");
        };

        var result = await Startup.RunJobAsync(MakeConfig(),
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public async Task RunAsync_TreatsNonNumericCost_AsZero()
    {
        const string resultLine = """{"type":"result","total_cost_usd":"not-a-number"}""";

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") onLine?.Invoke(resultLine);
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await Startup.RunJobAsync(MakeConfig(),
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0m, doc.RootElement.GetProperty("costUsd").GetDecimal());
    }

    [TestMethod]
    public async Task RunAsync_ReturnsJobSuccessOutcome_WithoutWritingResultJson()
    {
        var outcome = await JobRunner.RunAsync(MakeConfig(),
            Effects(new StubRepositoryHost(), FakeRunner(), _ => Task.FromResult<InstallResult>(new Installed())),
            CancellationToken.None);

        Assert.AreEqual(ExitCodes.Success, outcome.ExitCode);
        Assert.IsInstanceOfType<JobSuccess>(outcome.Result);
        Assert.IsFalse(File.Exists(Path.Combine(_outputDir, "result.json")),
            "JobRunner core must not perform the result.json write — that is the shell's job");
    }

    [TestMethod]
    public async Task RunAsync_ReturnsSetupFailedOutcome_WhenInstallerFails()
    {
        var outcome = await JobRunner.RunAsync(MakeConfig(),
            Effects(new StubRepositoryHost(), FakeRunner(), _ => Task.FromResult<InstallResult>(new InstallFailed("nope"))),
            CancellationToken.None);

        Assert.AreEqual(ExitCodes.SetupFailed, outcome.ExitCode);
        Assert.IsInstanceOfType<JobFailure>(outcome.Result);
    }

    // ---- helpers ----

    private static JobEffects Effects(
        IRepositoryHost host,
        RunProcessAsync processRunner,
        Func<CancellationToken, Task<InstallResult>> claudeInstaller,
        Action<string>? logLine = null) =>
        new(host, processRunner, claudeInstaller, logLine ?? (_ => { }));

    private Task<int> Run(
        int claudeExitCode = 0,
        bool claudeTimedOut = false,
        QueuedPrSpec? pr = null) =>
        Startup.RunJobAsync(MakeConfig(),
            host: new StubRepositoryHost(),
            processRunner: FakeRunner(claudeExitCode, claudeTimedOut, pr),
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

    private JobConfig MakeConfig() => JobConfig.FromInputs(
        repo: "owner/repo",
        prompt: "Do something",
        readToken: "tok",
        maxTokens: null,
        timeoutMinutes: null,
        workDir: _workDir,
        outputDir: _outputDir);

    private static RunProcessAsync FakeRunner(
        int claudeExitCode = 0,
        bool claudeTimedOut = false,
        QueuedPrSpec? pr = null) =>
        async (fileName, args, workDir, envOverrides, onLine, ct) => fileName switch
        {
            "claude" => await SimulateClaudeAsync(claudeExitCode, claudeTimedOut, pr, args, ct),
            "git" => await SimulateGitBundleAsync(args),
            _ => throw new NotSupportedException($"Unexpected process: {fileName}"),
        };

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
        return (end < 0 ? prompt[start..] : prompt[start..end]).Trim();
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
        return exitCode == 0 ? new ProcessSuccess() : (ProcessResult)new ProcessFailure($"exited with code {exitCode}");
    }

    private static async Task<ProcessResult> SimulateGitBundleAsync(IEnumerable<string> args)
    {
        var bundlePath = args.ElementAt(2); // git bundle create <path> <range>
        await File.WriteAllTextAsync(bundlePath, "fake-bundle");
        return new ProcessSuccess();
    }

    private record QueuedPrSpec(string Branch, string BaseBranch, string Title, string Body);

    private sealed class TrackingRepositoryHost : IRepositoryHost
    {
        public bool CloneCalled { get; private set; }
        public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
        {
            CloneCalled = true;
            return Task.CompletedTask;
        }
        public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }
}
