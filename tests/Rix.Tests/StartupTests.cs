using Rix.Submit;
using System.Runtime.InteropServices;

namespace Rix.Tests;

[TestClass]
public class StartupTests
{
    [TestMethod]
    public async Task RunAsync_WithHelpFlag_ReturnsZero()
    {
        var exitCode = await Startup.RunAsync(["--help"]);
        Assert.AreEqual(0, exitCode);
    }

    /// <summary><c>agent</c> and <c>ci-failure</c> register the same static <c>AgentOptions</c>
    /// instances, so both of them living under one root is the case that would break if a shared
    /// option could belong to only one command. Pinned explicitly rather than left to the help test,
    /// which would fail for this reason without naming it.</summary>
    [TestMethod]
    public async Task RunAsync_BuildsBothCommands_ThatShareAgentOptionInstances()
    {
        Assert.AreEqual(0, await Startup.RunAsync(["agent", "--help"]));
        Assert.AreEqual(0, await Startup.RunAsync(["ci-failure", "--help"]));
    }

    [TestMethod]
    public async Task RunAgentAsync_Returns2_WhenConfigIsInvalid()
    {
        var exitCode = await Startup.RunAsync(["agent"]);
        Assert.AreEqual(2, exitCode);
    }

    [TestMethod]
    public async Task RunAgentAsync_Returns2_WhenRepoFormatIsInvalid()
    {
        var exitCode = await Startup.RunAsync(
            ["agent", "--repo", "invalid-no-slash", "--prompt", "p", "--read-token", "r",
             "--output-dir", Path.GetTempPath()]);
        Assert.AreEqual(2, exitCode);
    }

    [TestMethod]
    public async Task RunInitializeAsync_WritesCallerWorkflows_AndReturnsZero()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        var exitCode = await Startup.RunAsync(["initialize", "--dir", dir]);

        Assert.AreEqual(0, exitCode);
        Assert.IsTrue(File.Exists(Path.Combine(dir, ".github/workflows/rix.yml")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, ".github/workflows/rix-on-ci-failure.yml")));
    }

    [TestMethod]
    public async Task RunInitializeAsync_Returns2_WhenTargetDirDoesNotExist()
    {
        using var stderr = new ConsoleErrorScope();
        var exitCode = await Startup.RunAsync(["initialize", "--dir", "/nonexistent/path/xyz"]);

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(stderr.Text, "error: --dir: directory does not exist: /nonexistent/path/xyz");
    }

    /// <summary>Building the config is what refuses it, so a missing directory costs no clone and no
    /// connection: the tokens here would be refused by GitHub if they ever got that far.</summary>
    [TestMethod]
    public async Task RunSubmitAsync_Returns2_WhenInputDirDoesNotExist()
    {
        using var stderr = new ConsoleErrorScope();
        var exitCode = await Startup.RunAsync
        (
            ["submit", "--repo", "owner/repo", "--write-token", "not-a-token", "--input-dir", "/nonexistent/in"]
        );

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(stderr.Text, "error: --input-dir: directory does not exist: /nonexistent/in");
    }

    /// <summary>The one command whose production wiring a run can reach without touching anything
    /// outside the machine: an input directory with no <c>result.json</c> is refused before the
    /// clone, so the real <see cref="SubmitContext"/> is built and run, and the token here is never
    /// offered to GitHub. Covers the path from the command list down to the exit code, which the
    /// stub-context tests in <see cref="SubmitCommandTests"/> deliberately start below.</summary>
    [TestMethod]
    public async Task RunSubmitAsync_Returns1_WhenInputDirHoldsNoResultJson()
    {
        var inputDir = Directory.CreateTempSubdirectory("rix-submit-in-").FullName;

        var exitCode = await Startup.RunAsync
        (
            ["submit", "--repo", "owner/repo", "--write-token", "not-a-token", "--input-dir", inputDir]
        );

        Assert.AreEqual(1, exitCode);
    }

    [TestMethod]
    public void HandleSigterm_CancelsTokenAndSuppressesDefaultTermination()
    {
        using var cts = new CancellationTokenSource();
        var ctx = new PosixSignalContext(PosixSignal.SIGTERM);

        Startup.HandleSigterm(cts)(ctx);

        Assert.IsTrue(cts.IsCancellationRequested);
        Assert.IsTrue(ctx.Cancel);
    }
}
