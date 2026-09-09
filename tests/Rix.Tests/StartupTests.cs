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
        var exitCode = await Startup.RunAsync(["initialize", "--dir", "/nonexistent/path/xyz"]);
        Assert.AreEqual(2, exitCode);
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
