using Rix.Agents;
using Rix.Job;
using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class OpenCodeAgentTests
{
    private static readonly OpenCodeAgent Agent = new();

    // Adapts a simple (fileName, args) -> ProcessResult stub to the full RunProcessAsync shape.
    private static RunProcessAsync Runner(Func<string, IEnumerable<string>, Task<ProcessResult>> run) =>
        (fileName, args, _, _, _, _) => run(fileName, args);

    // ---- EnsureInstalledAsync ----

    [TestMethod]
    public async Task EnsureInstalled_ReturnsInstalled_WhenOpenCodeAlreadyInstalled()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, _) => Task.FromResult<ProcessResult>(f == "opencode"
                ? new ProcessSuccess()
                : new ProcessFailure("exited with code 1"))),
            CancellationToken.None);

        Assert.IsInstanceOfType<Installed>(result);
    }

    [TestMethod]
    public async Task EnsureInstalled_ReturnsInstalled_WhenInstallSucceeds()
    {
        int opencodeCallCount = 0;
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, args) => f switch
            {
                "opencode" => Task.FromResult<ProcessResult>(++opencodeCallCount == 1
                    ? new ProcessFailure("exited with code 1")
                    : new ProcessSuccess()),
                "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                _ => throw new NotSupportedException(f),
            }),
            CancellationToken.None);

        Assert.IsInstanceOfType<Installed>(result);
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenNpmUnavailable()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((_, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1"))),
            CancellationToken.None);

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "npm");
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenNpmInstallFails()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, args) => f switch
            {
                "opencode" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" when args.Contains("--version") => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                "npm" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                _ => throw new NotSupportedException(f),
            }),
            CancellationToken.None);

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "npm install");
    }

    [TestMethod]
    public async Task EnsureInstalled_InstallsOpenCodePackage()
    {
        string? installedPackage = null;
        int opencodeCallCount = 0;

        await Agent.EnsureInstalledAsync(
            Runner((f, args) =>
            {
                if (f == "npm" && args.Contains("install"))
                    installedPackage = args.FirstOrDefault(a => a.StartsWith("opencode-ai"));
                return f switch
                {
                    "opencode" => Task.FromResult<ProcessResult>(++opencodeCallCount == 1
                        ? new ProcessFailure("exited with code 1")
                        : new ProcessSuccess()),
                    "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                    _ => throw new NotSupportedException(f),
                };
            }),
            CancellationToken.None);

        Assert.AreEqual("opencode-ai", installedPackage);
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenPostInstallOpenCodeCheckFails()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, args) => f switch
            {
                "opencode" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                _ => throw new NotSupportedException(f),
            }),
            CancellationToken.None);

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "could not be verified");
    }

    // ---- BuildInvocation / ParseCost ----

    [TestMethod]
    public void BuildInvocation_ProducesOpenCodeRunInvocation()
    {
        var config = JobConfig.FromInputs("owner/repo", "do it", "tok",
            maxTokens: 1234, timeoutMinutes: null, workDir: Path.GetTempPath(), outputDir: Path.GetTempPath());

        var invocation = Agent.BuildInvocation(config, "SYSTEM");

        Assert.AreEqual("opencode", invocation.FileName);
        var args = invocation.Arguments.ToList();
        CollectionAssert.Contains(args, "run");
        CollectionAssert.Contains(args, "--format");
        CollectionAssert.Contains(args, "json");
        // System prompt is folded into the run message ahead of the user prompt.
        Assert.IsTrue(args.Any(a => a.Contains("SYSTEM") && a.Contains("do it")));
        // OpenCode has no output-token cap equivalent, so no environment overrides are set.
        Assert.AreEqual(0, invocation.EnvironmentOverrides.Count);
    }

    [TestMethod]
    public void ParseCost_ForwardsToOpenCodeCost()
    {
        Assert.AreEqual(0.5m, Agent.ParseCost("""{"type":"step_finish","part":{"cost":0.5}}"""));
        Assert.IsNull(Agent.ParseCost("not json"));
    }
}
