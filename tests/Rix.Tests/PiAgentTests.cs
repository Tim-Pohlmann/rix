using Rix.Agents;
using Rix.Job;
using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class PiAgentTests
{
    private static readonly PiAgent Agent = new();

    // Adapts a simple (fileName, args) -> ProcessResult stub to the full RunProcessAsync shape.
    private static RunProcessAsync Runner(Func<string, IEnumerable<string>, Task<ProcessResult>> run)
    => (fileName, args, _, _, _, _) => run(fileName, args);

    // ---- EnsureInstalledAsync ----

    [TestMethod]
    public async Task EnsureInstalled_ReturnsInstalled_WhenPiAlreadyInstalled()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, _) => Task.FromResult<ProcessResult>(f == "pi"
                ? new ProcessSuccess()
                : new ProcessFailure("exited with code 1"))),
            CancellationToken.None);

        Assert.IsInstanceOfType<Installed>(result);
    }

    [TestMethod]
    public async Task EnsureInstalled_ReturnsInstalled_WhenInstallSucceeds()
    {
        int piCallCount = 0;
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, args) => f switch
            {
                "pi" => Task.FromResult<ProcessResult>(++piCallCount == 1
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
                "pi" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" when args.Contains("--version") => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                "npm" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                _ => throw new NotSupportedException(f),
            }),
            CancellationToken.None);

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "npm install");
    }

    [TestMethod]
    public async Task EnsureInstalled_InstallsPiPackage()
    {
        string? installedPackage = null;
        int piCallCount = 0;

        await Agent.EnsureInstalledAsync(
            Runner((f, args) =>
            {
                if (f == "npm" && args.Contains("install"))
                    installedPackage = args.FirstOrDefault(a => a.StartsWith("@earendil-works/pi-coding-agent"));
                return f switch
                {
                    "pi" => Task.FromResult<ProcessResult>(++piCallCount == 1
                        ? new ProcessFailure("exited with code 1")
                        : new ProcessSuccess()),
                    "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                    _ => throw new NotSupportedException(f),
                };
            }),
            CancellationToken.None);

        Assert.AreEqual("@earendil-works/pi-coding-agent", installedPackage);
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenPostInstallPiCheckFails()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, args) => f switch
            {
                "pi" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                _ => throw new NotSupportedException(f),
            }),
            CancellationToken.None);

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "could not be verified");
    }

    // ---- BuildInvocation / ParseCost ----

    [TestMethod]
    public void BuildInvocation_ProducesPiJsonModeInvocation()
    {
        var config = TestConfig.Valid(agent: "pi", maxTokens: 1234);

        var invocation = Agent.BuildInvocation(config, "SYSTEM");

        Assert.AreEqual("pi", invocation.FileName);
        var args = invocation.Arguments.ToList();
        CollectionAssert.Contains(args, "--mode");
        CollectionAssert.Contains(args, "json");
        CollectionAssert.Contains(args, "--append-system-prompt");
        CollectionAssert.Contains(args, "SYSTEM");
        CollectionAssert.Contains(args, "do it");
        // Pi has no output-token cap equivalent, so no environment overrides are set.
        Assert.AreEqual(0, invocation.EnvironmentOverrides.Count);
    }

    [TestMethod]
    public void BuildInvocation_OmitsModelFlag_ByDefault()
    {
        var config = TestConfig.Valid(agent: "pi");

        var args = Agent.BuildInvocation(config, "SYSTEM").Arguments.ToList();

        CollectionAssert.DoesNotContain(args, "--model");
    }

    [TestMethod]
    public void BuildInvocation_IncludesModelFlag_WhenModelOverridden()
    {
        var config = TestConfig.Valid(agent: "pi", model: "openai/gpt-4o");

        var args = Agent.BuildInvocation(config, "SYSTEM").Arguments.ToList();

        var modelIndex = args.IndexOf("--model");
        Assert.AreNotEqual(-1, modelIndex);
        Assert.AreEqual("openai/gpt-4o", args[modelIndex + 1]);
    }
}
