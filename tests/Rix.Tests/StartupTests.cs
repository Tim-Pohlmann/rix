using Rix.Cli;
using Rix.Submit;
using System.Runtime.InteropServices;

namespace Rix.Tests;

[TestClass]
public class StartupTests
{
    private static string Shell => OperatingSystem.IsWindows() ? "pwsh" : "/bin/sh";

    private static string Echo(string text) =>
        OperatingSystem.IsWindows() ? $"Write-Output '{text}'" : $"echo {text}";

    [TestMethod]
    public async Task RunAsync_WithHelpFlag_ReturnsZero()
    {
        var exitCode = await Startup.RunAsync(["--help"]);
        Assert.AreEqual(0, exitCode);
    }

    /// <summary><c>job</c> and <c>ci-failure</c> register the same static <c>JobOptions</c>
    /// instances, so both of them living under one root is the case that would break if a shared
    /// option could belong to only one command. Pinned explicitly rather than left to the help test,
    /// which would fail for this reason without naming it.</summary>
    [TestMethod]
    public async Task RunAsync_BuildsBothCommands_ThatShareJobOptionInstances()
    {
        Assert.AreEqual(0, await Startup.RunAsync(["job", "--help"]));
        Assert.AreEqual(0, await Startup.RunAsync(["ci-failure", "--help"]));
    }

    [TestMethod]
    public async Task RunJobAsync_Returns2_WhenConfigIsInvalid()
    {
        var exitCode = await Startup.RunAsync(["job"]);
        Assert.AreEqual(2, exitCode);
    }

    [TestMethod]
    public async Task RunJobAsync_Returns2_WhenRepoFormatIsInvalid()
    {
        var exitCode = await Startup.RunAsync(
            ["job", "--repo", "invalid-no-slash", "--prompt", "p", "--read-token", "r",
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
        StringAssert.Contains(stderr.Text, $"error: --dir: directory does not exist: {Path.GetFullPath("/nonexistent/path/xyz")}");
    }

    /// <summary>The check runs before the command does, so a missing directory costs no clone and no
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
        StringAssert.Contains(stderr.Text, $"error: --input-dir: directory does not exist: {Path.GetFullPath("/nonexistent/in")}");
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
    public async Task WhenDirectoriesExist_Runs_WhenEveryDirectoryExists()
    {
        var run = Startup.WhenDirectoriesExist<int>
        (
            new StubFileSystem(directoryExists: _ => true),
            _ => [new RequiredDirectory("--work-dir", new DirectoryPath("/only/on/the/stub"))],
            (config, _) => Task.FromResult(config)
        );

        var exitCode = await run(7, CancellationToken.None);

        Assert.AreEqual(7, exitCode);
    }

    [TestMethod]
    public void WhenDirectoriesExist_ReportsTheFirstMissingDirectory_WithoutRunning()
    {
        var ran = false;
        var present = new DirectoryPath("/present");
        var missing = new DirectoryPath("/missing");

        var run = Startup.WhenDirectoriesExist<int>
        (
            new StubFileSystem(directoryExists: path => path == present.Value),
            _ => [new RequiredDirectory("--work-dir", present), new RequiredDirectory("--output-dir", missing), new RequiredDirectory("--dir", missing)],
            (_, _) =>
            {
                ran = true;
                return Task.FromResult(0);
            }
        );

        var ex = Assert.ThrowsExactly<InvalidInputException>
        (
            () => _ = run(0, CancellationToken.None)
        );

        Assert.AreEqual($"--output-dir: directory does not exist: {missing.Value}", ex.Message);
        Assert.IsFalse(ran);
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
