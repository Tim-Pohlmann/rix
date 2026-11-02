using Rix.Cli;
using Rix.Submit;

namespace Rix.Tests;

[TestClass]
public class SubmitCommandTests
{
    private static readonly string ExistingDir = Path.GetTempPath();
    private static readonly string[] ExpectedTrimmedBranches = ["main", "release/1"];
    private static readonly string[] ExpectedUnusualBranches = ["--not-a-flag", "feature/ünïcode"];

    private static SubmitConfig ReadArgs(params string[] args)
    => SubmitCommand.ReadConfig(CommandArgs.Parse(SubmitCommand.Build(), ["submit", .. args]));

    /// <summary>Reads <c>submit</c> with the given flags, filling in valid values for the required
    /// ones the test doesn't care about.</summary>
    private static SubmitConfig Read
    (
        string repo = "owner/repo",
        string writeToken = "write-tok",
        string? inputDir = null,
        string? workDir = null,
        string? allowedPushBranches = null
    )
    {
        var args = new List<string> { "--repo", repo, "--write-token", writeToken, "--input-dir", inputDir ?? ExistingDir };
        if (workDir is not null)
            args.AddRange(["--work-dir", workDir]);
        if (allowedPushBranches is not null)
            args.AddRange(["--allowed-push-branches", allowedPushBranches]);
        return ReadArgs([.. args]);
    }

    [TestMethod]
    public void Command_BuildsConfig_ForValidInputs()
    {
        var config = Read();

        Assert.AreEqual("owner/repo", config.Repo.ToString());
        Assert.AreEqual("write-tok", config.WriteToken.Value);
        Assert.AreEqual(Path.GetTempPath(), config.InputDir.Value);
    }

    [TestMethod]
    public void Command_PassesEnvVarFallbacks_WhenFlagsAbsent()
    {
        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        env.Set("RIX_WRITE_TOKEN", "env-write");
        env.Set("RIX_INPUT_DIR", ExistingDir);
        var config = ReadArgs();

        Assert.AreEqual("env/repo", config.Repo.ToString());
        Assert.AreEqual("env-write", config.WriteToken.Value);
    }

    [TestMethod]
    public void Command_DefaultsWorkDirToTemp_WhenAbsentOrBlank()
    {
        Assert.AreEqual(Path.GetTempPath(), Read().WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), Read(workDir: "   ").WorkDir.Value);
    }

    [TestMethod]
    [DataRow("", "write-tok", null, "--repo is required")]
    [DataRow("noslash", "write-tok", null, "--repo: 'noslash' is not a valid repo identifier")]
    [DataRow("owner/repo/extra", "write-tok", null, "--repo: 'owner/repo/extra' is not a valid repo identifier")]
    [DataRow("owner/repo", "", null, "--write-token is required")]
    [DataRow("owner/repo", "write-tok", "", "--input-dir is required")]
    [DataRow("owner/repo", "write-tok", "/nonexistent/in", "--input-dir: directory does not exist: /nonexistent/in")]
    public void Command_ReportsTheFlag_WhenInputInvalid(string repo, string writeToken, string? inputDir, string expectedError)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read(repo: repo, writeToken: writeToken, inputDir: inputDir));

        StringAssert.StartsWith(ex.Message, expectedError);
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenWorkDirDoesNotExist()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read(workDir: "/nonexistent/path/xyz"));

        Assert.AreEqual("--work-dir: directory does not exist: /nonexistent/path/xyz", ex.Message);
    }

    [TestMethod]
    public void Command_AllowsNoPushBranches_WhenTheListIsUnset()
    {
        Assert.AreEqual(0, Read().AllowedPushBranches.Count);
        Assert.AreEqual(0, Read(allowedPushBranches: "   ").AllowedPushBranches.Count);
    }

    [TestMethod]
    public void Command_SplitsAllowedPushBranches_TrimmingAndDroppingDuplicates()
    {
        var config = Read(allowedPushBranches: " main , release/1 ,main, ");

        CollectionAssert.AreEqual(ExpectedTrimmedBranches, config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    /// <summary>An unusable entry is not an error: every string is a possible branch name, so the
    /// list can only ever be over- or under-inclusive, and the safe direction is taken silently.</summary>
    [TestMethod]
    public void Command_KeepsAnyBranchName_InAllowedPushBranches()
    {
        var config = Read(allowedPushBranches: "--not-a-flag,feature/ünïcode");

        CollectionAssert.AreEqual(ExpectedUnusualBranches, config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }
}
