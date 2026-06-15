using Rix.Agents;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

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
    public async Task DefaultRunProcess_RunsRealProcess_AndCapturesOutput()
    {
        var lines = new List<string>();

        var result = await Startup.DefaultRunProcess(
            Shell, ["-c", Echo("hi")], Path.GetTempPath(), null, lines.Add, CancellationToken.None);

        Assert.IsInstanceOfType<ProcessSuccess>(result);
        Assert.IsTrue(lines.Any(l => l.Contains("hi")), $"Got: [{string.Join(", ", lines)}]");
    }

    [TestMethod]
    public void DefaultContext_UsesGitHubHostAndDefaultRunProcess()
    {
        var context = Startup.DefaultContext(TestConfig.Valid());

        Assert.IsInstanceOfType<GitHubRepositoryHost>(context.Host);
        Assert.AreSame(Startup.DefaultRunProcess, context.RunProcess);
        Assert.IsInstanceOfType<ClaudeAgent>(context.Agent);
    }
}
