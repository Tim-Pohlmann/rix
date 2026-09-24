using Rix.Cli;
using Rix.Submit;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class SubmitCommandTests
{
    private static readonly string ExistingDir = Path.GetTempPath();
    private static readonly string[] ExpectedTrimmedBranches = ["main", "release/1"];
    private static readonly string[] ExpectedUnusualBranches = ["--not-a-flag", "feature/ünïcode"];

    private static Parser BuildParser(Func<SubmitConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(SubmitCommand.Build(handler));
        return CliPipeline.Build(root);
    }

    /// <summary>Runs <c>submit</c> with the given flags; returns the config the handler received
    /// (null when the command rejected the input before reaching it), the exit code, and stderr.</summary>
    private static async Task<(SubmitConfig? Config, int ExitCode, string Stderr)> RunAsync
    (
        string repo = "owner/repo",
        string writeToken = "write-tok",
        string? inputDir = null,
        string? workDir = null,
        string? allowedPushBranches = null
    )
    {
        SubmitConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });
        var args = new List<string> { "submit", "--repo", repo, "--write-token", writeToken, "--input-dir", inputDir ?? ExistingDir };
        if (workDir is not null)
            args.AddRange(["--work-dir", workDir]);
        if (allowedPushBranches is not null)
            args.AddRange(["--allowed-push-branches", allowedPushBranches]);

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync([.. args]);
        return (captured, exitCode, stderr.Text);
    }

    [TestMethod]
    public async Task Command_BuildsConfig_ForValidInputs()
    {
        var (config, exitCode, _) = await RunAsync();

        Assert.AreEqual(0, exitCode);
        Assert.IsNotNull(config);
        Assert.AreEqual("owner/repo", config.Repo.ToString());
        Assert.AreEqual("write-tok", config.WriteToken.Value);
        Assert.AreEqual(Path.GetTempPath(), config.InputDir.Value);
    }

    [TestMethod]
    public async Task Command_PassesEnvVarFallbacks_WhenFlagsAbsent()
    {
        SubmitConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        env.Set("RIX_WRITE_TOKEN", "env-write");
        env.Set("RIX_INPUT_DIR", ExistingDir);
        await parser.InvokeAsync("submit");

        Assert.IsNotNull(captured);
        Assert.AreEqual("env/repo", captured.Repo.ToString());
        Assert.AreEqual("env-write", captured.WriteToken.Value);
    }

    [TestMethod]
    public async Task Command_DefaultsWorkDirToTemp_WhenAbsentOrBlank()
    {
        Assert.AreEqual(Path.GetTempPath(), (await RunAsync()).Config?.WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), (await RunAsync(workDir: "   ")).Config?.WorkDir.Value);
    }

    [TestMethod]
    [DataRow("", "write-tok", null, "--repo is required")]
    [DataRow("noslash", "write-tok", null, "--repo: 'noslash' is not a valid repo identifier")]
    [DataRow("owner/repo/extra", "write-tok", null, "--repo: 'owner/repo/extra' is not a valid repo identifier")]
    [DataRow("owner/repo", "", null, "--write-token is required")]
    [DataRow("owner/repo", "write-tok", "", "--input-dir is required")]
    [DataRow("owner/repo", "write-tok", "/nonexistent/in", "--input-dir: directory does not exist: /nonexistent/in")]
    public async Task Command_Returns2_AndReportsTheFlag_WhenInputInvalid(string repo, string writeToken, string? inputDir, string expectedError)
    {
        var (config, exitCode, stderr) = await RunAsync(repo: repo, writeToken: writeToken, inputDir: inputDir);

        Assert.IsNull(config);
        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr, $"error: {expectedError}");
    }

    [TestMethod]
    public async Task Command_Returns2_WhenWorkDirDoesNotExist()
    {
        var (config, exitCode, stderr) = await RunAsync(workDir: "/nonexistent/path/xyz");

        Assert.IsNull(config);
        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr, "error: --work-dir: directory does not exist: /nonexistent/path/xyz");
    }

    [TestMethod]
    public async Task Command_AllowsNoPushBranches_WhenTheListIsUnset()
    {
        Assert.AreEqual(0, (await RunAsync()).Config?.AllowedPushBranches.Count);
        Assert.AreEqual(0, (await RunAsync(allowedPushBranches: "   ")).Config?.AllowedPushBranches.Count);
    }

    [TestMethod]
    public async Task Command_SplitsAllowedPushBranches_TrimmingAndDroppingDuplicates()
    {
        var config = (await RunAsync(allowedPushBranches: " main , release/1 ,main, ")).Config;

        Assert.IsNotNull(config);
        CollectionAssert.AreEqual(ExpectedTrimmedBranches, config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    /// <summary>An unusable entry is not an error: every string is a possible branch name, so the
    /// list can only ever be over- or under-inclusive, and the safe direction is taken silently.</summary>
    [TestMethod]
    public async Task Command_KeepsAnyBranchName_InAllowedPushBranches()
    {
        var config = (await RunAsync(allowedPushBranches: "--not-a-flag,feature/ünïcode")).Config;

        Assert.IsNotNull(config);
        CollectionAssert.AreEqual(ExpectedUnusualBranches, config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }
}
