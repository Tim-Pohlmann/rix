using Rix.Job;
using Rix.Process;

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
    public async Task DefaultRunProcess_RunsRealProcess_AndCapturesOutput()
    {
        var lines = new List<string>();

        var result = await Startup.DefaultRunProcess(
            Shell, ["-c", Echo("hi")], Path.GetTempPath(), null, lines.Add, CancellationToken.None);

        Assert.IsInstanceOfType<ProcessSuccess>(result);
        Assert.IsTrue(lines.Any(l => l.Contains("hi")), $"Got: [{string.Join(", ", lines)}]");
    }

    [TestMethod]
    public void DefaultContext_WiresAllCollaborators()
    {
        var config = JobConfig.FromInputs(
            repo: "owner/repo", prompt: "do it", readToken: "tok",
            maxTokens: null, timeoutMinutes: null,
            workDir: Path.GetTempPath(), outputDir: Path.GetTempPath());

        var context = Startup.DefaultContext(config);

        Assert.IsNotNull(context.Host);
        Assert.IsNotNull(context.RunProcess);
        Assert.IsNotNull(context.InstallClaude);
        Assert.IsNotNull(context.LogLine);
    }
}
