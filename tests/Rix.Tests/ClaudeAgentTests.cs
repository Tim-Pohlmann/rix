using Rix.Agents;
using Rix.Job;
using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class ClaudeAgentTests
{
    private static readonly ClaudeAgent Agent = new();

    // Adapts a simple (fileName, args) -> ProcessResult stub to the full RunProcessAsync shape.
    private static RunProcessAsync Runner(Func<string, IEnumerable<string>, Task<ProcessResult>> run)
    => (fileName, args, _, _, _, _) => run(fileName, args);

    // ---- EnsureInstalledAsync ----

    [TestMethod]
    public async Task EnsureInstalled_ReturnsInstalled_WhenClaudeAlreadyInstalled()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, _) => Task.FromResult<ProcessResult>(f == "claude"
                ? new ProcessSuccess()
                : new ProcessFailure("exited with code 1"))),
            CancellationToken.None);

        Assert.IsInstanceOfType<Installed>(result);
    }

    [TestMethod]
    public async Task EnsureInstalled_ReturnsInstalled_WhenInstallSucceeds()
    {
        int claudeCallCount = 0;
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, args) => f switch
            {
                "claude" => Task.FromResult<ProcessResult>(++claudeCallCount == 1
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
                "claude" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" when args.Contains("--version") => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                "npm" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                _ => throw new NotSupportedException(f),
            }),
            CancellationToken.None);

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "npm install");
    }

    [TestMethod]
    public async Task EnsureInstalled_InstallsLatestVersion()
    {
        string? installedPackage = null;
        int claudeCallCount = 0;

        await Agent.EnsureInstalledAsync(
            Runner((f, args) =>
            {
                if (f == "npm" && args.Contains("install"))
                    installedPackage = args.FirstOrDefault(a => a.StartsWith("@anthropic-ai/claude-code"));
                return f switch
                {
                    "claude" => Task.FromResult<ProcessResult>(++claudeCallCount == 1
                        ? new ProcessFailure("exited with code 1")
                        : new ProcessSuccess()),
                    "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                    _ => throw new NotSupportedException(f),
                };
            }),
            CancellationToken.None);

        Assert.AreEqual("@anthropic-ai/claude-code", installedPackage);
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenTimesOut()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((_, _) => Task.FromResult<ProcessResult>(new ProcessFailure("timed out"))),
            CancellationToken.None);

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "timed out");
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenPostInstallClaudeCheckFails()
    {
        var result = await Agent.EnsureInstalledAsync(
            Runner((f, args) => f switch
            {
                "claude" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                _ => throw new NotSupportedException(f),
            }),
            CancellationToken.None);

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "could not be verified");
    }

    // ---- BuildInvocation / ParseCost ----

    [TestMethod]
    public void BuildInvocation_ProducesClaudePrintInvocation()
    {
        var config = TestConfig.Valid(agent: "claude", maxTokens: "1234");

        var invocation = Agent.BuildInvocation(config, "SYSTEM");

        Assert.AreEqual("claude", invocation.FileName);
        CollectionAssert.Contains(invocation.Arguments.ToList(), "--append-system-prompt");
        CollectionAssert.Contains(invocation.Arguments.ToList(), "SYSTEM");
        Assert.AreEqual("1234", invocation.EnvironmentOverrides["CLAUDE_CODE_MAX_OUTPUT_TOKENS"]);
    }

    [TestMethod]
    public void BuildInvocation_IncludesVerboseFlag()
    {
        // The real claude CLI rejects --print combined with --output-format=stream-json unless
        // --verbose is also present ("requires --verbose"); this guards against dropping it.
        var config = TestConfig.Valid(agent: "claude");

        var args = Agent.BuildInvocation(config, "SYSTEM").Arguments.ToList();

        CollectionAssert.Contains(args, "--verbose");
    }

    [TestMethod]
    public void BuildInvocation_IncludesModelFlag_WhenModelSet()
    {
        var config = TestConfig.Valid(agent: "claude", model: "claude-opus-4");

        var args = Agent.BuildInvocation(config, "SYSTEM").Arguments.ToList();

        var modelIndex = args.IndexOf("--model");
        Assert.AreNotEqual(-1, modelIndex);
        Assert.AreEqual("claude-opus-4", args[modelIndex + 1]);
    }

    [TestMethod]
    public void BuildInvocation_OmitsModelFlag_WhenModelNotSet()
    {
        var config = TestConfig.Valid(agent: "claude");

        var args = Agent.BuildInvocation(config, "SYSTEM").Arguments.ToList();

        CollectionAssert.DoesNotContain(args, "--model");
    }
}
