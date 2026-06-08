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
        var result = await JobRunner.RunAsync(
            MakeConfig(), CancellationToken.None,
            processRunner: FakeRunner(),
            claudeInstaller: _ => Task.FromResult<InstallResult>(new InstallFailed("install failed")));

        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public async Task RunAsync_DoesNotClone_WhenClaudeInstallerFails()
    {
        var host = new TrackingRepositoryHost();

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: host, processRunner: FakeRunner(),
            claudeInstaller: _ => Task.FromResult<InstallResult>(new InstallFailed("install failed")));

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

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: host, processRunner: FakeRunner(),
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

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

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: tracker,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

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

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: tracker,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

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

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: capture,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

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

            await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
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

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: capture,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        Assert.IsNotNull(systemPrompt);
        StringAssert.Contains(systemPrompt, "A local API is available at http://");
        StringAssert.Contains(systemPrompt, "/pr");
    }

    [TestMethod]
    public async Task RunAsync_ExtractsTokensUsed_FromClaudeResultLine()
    {
        const string resultLine = """{"type":"result","subtype":"success","total_input_tokens":1000,"total_output_tokens":500}""";

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") onLine?.Invoke(resultLine);
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(1500, doc.RootElement.GetProperty("tokensUsed").GetInt32());
    }

    [TestMethod]
    public async Task RunAsync_AccumulatesTokensUsed_AcrossMultipleResultLines()
    {
        const string line1 = """{"type":"result","total_input_tokens":1000,"total_output_tokens":500}""";
        const string line2 = """{"type":"result","total_input_tokens":200,"total_output_tokens":100}""";

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") { onLine?.Invoke(line1); onLine?.Invoke(line2); }
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(1800, doc.RootElement.GetProperty("tokensUsed").GetInt32());
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

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0, doc.RootElement.GetProperty("tokensUsed").GetInt32());
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

        var result = await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public async Task RunAsync_TreatsNonIntegerTokenFields_AsZero()
    {
        const string resultLine = """{"type":"result","total_input_tokens":"not-a-number","total_output_tokens":null}""";

        RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") onLine?.Invoke(resultLine);
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: runner,
            claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

        var json = await File.ReadAllTextAsync(Path.Combine(_outputDir, "result.json"));
        var doc = JsonDocument.Parse(json);
        Assert.AreEqual(0, doc.RootElement.GetProperty("tokensUsed").GetInt32());
    }

    // ---- helpers ----

    private Task<int> Run(
        int claudeExitCode = 0,
        bool claudeTimedOut = false,
        QueuedPrSpec? pr = null) =>
        JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
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
