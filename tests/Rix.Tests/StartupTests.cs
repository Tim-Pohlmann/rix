using Rix.Agents;
using Rix.Cli;
using Rix.Job;
using Rix.Repository;
using System.Runtime.InteropServices;

namespace Rix.Tests;

[TestClass]
public class StartupTests
{
    private static string Shell => OperatingSystem.IsWindows() ? "pwsh" : "/bin/sh";

    private static string Echo(string text) =>
        OperatingSystem.IsWindows() ? $"Write-Output '{text}'" : $"echo {text}";

    /// <summary>The one thing a <see cref="JobContext"/> takes from its config: which coding agent
    /// to run. Every kind is pinned here rather than one per <c>[DataRow]</c>, which can't carry an
    /// internal <see cref="AgentKind"/> into a public test method — the mapping is a switch a new
    /// kind can be added to without anything else noticing.</summary>
    [TestMethod]
    public void DefaultContext_RunsTheAgentTheConfigNames()
    {
        Assert.IsInstanceOfType<ClaudeAgent>(Startup.DefaultContext(TestConfig.Valid(agent: AgentKind.Claude)).Agent);
        Assert.IsInstanceOfType<OpenCodeAgent>(Startup.DefaultContext(TestConfig.Valid(agent: AgentKind.OpenCode)).Agent);
        Assert.IsInstanceOfType<PiAgent>(Startup.DefaultContext(TestConfig.Valid(agent: AgentKind.Pi)).Agent);
    }

    /// <summary><c>ci-failure</c> builds its job half up front, before a failure has been detected
    /// and so before the <see cref="JobConfig"/> naming the agent exists — the agent has to come
    /// from the ci-failure config instead. Nothing here opens a connection, which is what makes
    /// building it before it's known to be needed free.</summary>
    [TestMethod]
    public void DefaultCiFailureContext_RunsTheAgentTheCiFailureConfigNames()
    {
        var context = Startup.DefaultCiFailureContext(TestConfig.ValidCiFailure(agent: AgentKind.Claude));

        Assert.IsInstanceOfType<ClaudeAgent>(context.Job.Agent);
        Assert.IsInstanceOfType<GitHubActionsCiHost>(context.Ci);
        Assert.IsInstanceOfType<GitHubCiFailureRepoHost>(context.RepoHost);
    }

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

    [TestMethod]
    public async Task RunIfDirectoriesExist_Runs_WhenEveryDirectoryExists()
    {
        var exitCode = await Startup.RunIfDirectoriesExist
        (
            new StubFileSystem(directoryExists: _ => true),
            [new RequiredDirectory("--work-dir", new DirectoryPath("/only/on/the/stub"))],
            () => Task.FromResult(7)
        );

        Assert.AreEqual(7, exitCode);
    }

    [TestMethod]
    public void RunIfDirectoriesExist_ReportsTheFirstMissingDirectory_WithoutRunning()
    {
        var ran = false;
        var present = new DirectoryPath("/present");
        var missing = new DirectoryPath("/missing");

        var ex = Assert.ThrowsExactly<InvalidInputException>(() => _ = Startup.RunIfDirectoriesExist
        (
            new StubFileSystem(directoryExists: path => path == present.Value),
            [new RequiredDirectory("--work-dir", present), new RequiredDirectory("--output-dir", missing), new RequiredDirectory("--dir", missing)],
            () =>
            {
                ran = true;
                return Task.FromResult(0);
            }
        ));

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
