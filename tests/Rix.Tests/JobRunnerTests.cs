using System.Net.Http.Json;
using System.Text.Json;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class JobRunnerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

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
        if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true);
        if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, true);
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
        StringAssert.Contains(json, "rix-my-fix.bundle");
    }

    [TestMethod]
    public async Task RunAsync_CreatesBundleFile()
    {
        await Run(pr: new("rix/my-fix", "main", "My fix", "body"));

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "rix-my-fix.bundle")));
    }

    [TestMethod]
    public async Task RunAsync_SanitizesBranchNameInBundleFileName()
    {
        await Run(pr: new("rix/feat/sub", "main", "T", "b"));

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "rix-feat-sub.bundle")));
    }

    [TestMethod]
    public async Task RunAsync_ClonesRepo()
    {
        var host = new TrackingRepositoryHost();

        await JobRunner.RunAsync(MakeConfig(), host, FakeRunner(), CancellationToken.None);

        Assert.IsTrue(host.CloneCalled);
    }

    [TestMethod]
    public async Task RunAsync_CleansUpCloneDir()
    {
        string? capturedCloneDir = null;

        RunProcessAsync tracker = async (f, a, d, e, ct) =>
        {
            if (f == "claude") capturedCloneDir = d;
            return new ProcessResult(0, false);
        };

        await JobRunner.RunAsync(MakeConfig(), new StubRepositoryHost(), tracker, CancellationToken.None);

        Assert.IsNotNull(capturedCloneDir);
        Assert.IsFalse(Directory.Exists(capturedCloneDir), "Clone dir should be deleted after run");
    }

    [TestMethod]
    public async Task RunAsync_CleansUpCloneDir_EvenOnClaudeFailure()
    {
        string? capturedCloneDir = null;

        RunProcessAsync tracker = (f, a, d, e, ct) =>
        {
            capturedCloneDir = d;
            return Task.FromResult(new ProcessResult(1, false));
        };

        await JobRunner.RunAsync(MakeConfig(), new StubRepositoryHost(), tracker, CancellationToken.None);

        Assert.IsNotNull(capturedCloneDir);
        Assert.IsFalse(Directory.Exists(capturedCloneDir));
    }

    [TestMethod]
    public async Task RunAsync_PassesApiUrlToClaudeEnv()
    {
        string? apiUrl = null;

        RunProcessAsync capture = (f, a, d, e, ct) =>
        {
            if (f == "claude") apiUrl = e?["RIX_API_URL"];
            return Task.FromResult(new ProcessResult(0, false));
        };

        await JobRunner.RunAsync(MakeConfig(), new StubRepositoryHost(), capture, CancellationToken.None);

        Assert.IsNotNull(apiUrl);
        StringAssert.StartsWith(apiUrl, "http://");
    }

    // ---- helpers ----

    private Task<int> Run(
        int claudeExitCode = 0,
        bool claudeTimedOut = false,
        QueuedPrSpec? pr = null) =>
        JobRunner.RunAsync(MakeConfig(), new StubRepositoryHost(), FakeRunner(claudeExitCode, claudeTimedOut, pr), CancellationToken.None);

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
        async (fileName, args, workDir, envOverrides, ct) =>
        {
            if (fileName == "claude")
            {
                if (pr is not null && envOverrides is not null && envOverrides.TryGetValue("RIX_API_URL", out var apiUrl))
                {
                    using var client = new HttpClient();
                    await client.PostAsJsonAsync(new Uri(new Uri(apiUrl), "/pr"), new
                    {
                        branch = pr.Branch,
                        title = pr.Title,
                        body = pr.Body,
                        baseBranch = pr.BaseBranch,
                    });
                }
                return new ProcessResult(claudeTimedOut ? -1 : claudeExitCode, claudeTimedOut);
            }

            if (fileName == "git")
            {
                var bundlePath = args.ElementAt(2); // "bundle", "create", <path>, ...
                await File.WriteAllTextAsync(bundlePath, "fake-bundle");
                return new ProcessResult(0, false);
            }

            throw new NotSupportedException($"Unexpected process: {fileName}");
        };

    private record QueuedPrSpec(string Branch, string BaseBranch, string Title, string Body);

    private sealed class StubRepositoryHost : IRepositoryHost
    {
        public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

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
