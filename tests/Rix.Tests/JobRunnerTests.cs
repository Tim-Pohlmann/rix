using System.Net.Http.Json;
using System.Text.Json;
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
            claudeInstaller: _ => Task.FromResult(false));

        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public async Task RunAsync_DoesNotClone_WhenClaudeInstallerFails()
    {
        var host = new TrackingRepositoryHost();

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: host, processRunner: FakeRunner(),
            claudeInstaller: _ => Task.FromResult(false));

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
            claudeInstaller: _ => Task.FromResult(true));

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
            claudeInstaller: _ => Task.FromResult(true));

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
            claudeInstaller: _ => Task.FromResult(true));

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
                if (e is null || !e.TryGetValue("RIX_API_URL", out var apiUrl))
                    throw new InvalidOperationException("RIX_API_URL not set in Claude env.");
                var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
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
            claudeInstaller: _ => Task.FromResult(true));

        Assert.IsNotNull(claudeCallback);
        Assert.IsNull(gitCallback);
    }

    [TestMethod]
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
                claudeInstaller: _ => Task.FromResult(true));

            StringAssert.Contains(stderr.ToString(), "test line");
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [TestMethod]
    public async Task RunAsync_PassesApiUrlToClaudeEnv()
    {
        string? apiUrl = null;

        RunProcessAsync capture = (f, a, d, e, onLine, ct) =>
        {
            if (f == "claude") e?.TryGetValue("RIX_API_URL", out apiUrl);
            return Task.FromResult<ProcessResult>(new ProcessSuccess());
        };

        await JobRunner.RunAsync(MakeConfig(), CancellationToken.None,
            host: new StubRepositoryHost(), processRunner: capture,
            claudeInstaller: _ => Task.FromResult(true));

        Assert.IsNotNull(apiUrl);
        StringAssert.StartsWith(apiUrl, "http://");
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
            claudeInstaller: _ => Task.FromResult(true));

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
            claudeInstaller: _ => Task.FromResult(true));

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
            claudeInstaller: _ => Task.FromResult(true));

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
                var apiUrl = e?["RIX_API_URL"] ?? throw new InvalidOperationException("RIX_API_URL missing");
                var response = await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
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
            claudeInstaller: _ => Task.FromResult(true));

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
            claudeInstaller: _ => Task.FromResult(true));

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
            claudeInstaller: _ => Task.FromResult(true));

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
            "claude" => await SimulateClaudeAsync(claudeExitCode, claudeTimedOut, pr, envOverrides),
            "git" => await SimulateGitBundleAsync(args),
            _ => throw new NotSupportedException($"Unexpected process: {fileName}"),
        };

    private static async Task<ProcessResult> SimulateClaudeAsync(
        int exitCode, bool timedOut, QueuedPrSpec? pr, IReadOnlyDictionary<string, string>? envOverrides)
    {
        if (pr is not null && envOverrides is not null && envOverrides.TryGetValue("RIX_API_URL", out var apiUrl))
        {
            await HttpClient.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
            {
                branch = pr.Branch,
                title = pr.Title,
                body = pr.Body,
                baseBranch = pr.BaseBranch,
            }, CancellationToken.None);
        }
        return timedOut ? new ProcessFailure("timed out") : (exitCode == 0 ? new ProcessSuccess() : new ProcessFailure($"exited with code {exitCode}"));
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
