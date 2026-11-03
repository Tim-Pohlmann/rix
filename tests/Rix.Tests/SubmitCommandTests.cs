using Rix.Cli;
using Rix.Submit;

namespace Rix.Tests;

[TestClass]
public class SubmitCommandTests
{
    private static readonly string ExistingDir = Path.GetTempPath();
    private static readonly string PrivateTempDir = Directory.CreateTempSubdirectory("rix-submit-opts-").FullName;
    private static readonly string[] ExpectedTrimmedBranches = ["main", "release/1"];
    private static readonly string[] ExpectedUnusualBranches = ["--not-a-flag", "feature/ünïcode"];

    private static SubmitConfig ReadArgs(params string[] args)
    => SubmitCommand.CreateConfig(CommandArgs.Parse(SubmitCommand.Build(), ["submit", .. args]), new LocalFileSystem());

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

    /// <summary>Reads <c>submit</c> against <paramref name="fileSystem"/> rather than the disk, so
    /// the work-dir fallback can be observed without creating a directory of its own.</summary>
    private static SubmitConfig ReadWith(IFileSystem fileSystem, params string[] extra)
    => SubmitCommand.CreateConfig
    (
        CommandArgs.Parse
        (
            SubmitCommand.Build(),
            ["submit", "--repo", "owner/repo", "--write-token", "write-tok", "--input-dir", ExistingDir, .. extra]
        ),
        fileSystem
    );

    [TestMethod]
    public void Command_DefaultsWorkDirToAPrivateTempDirectory_WhenAbsentOrBlank()
    {
        var fileSystem = new StubFileSystem(privateTempDirectory: PrivateTempDir);

        Assert.AreEqual(PrivateTempDir, ReadWith(fileSystem).WorkDir.Value);
        Assert.AreEqual(PrivateTempDir, ReadWith(fileSystem, "--work-dir", "   ").WorkDir.Value);
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

    /// <summary>Both outcomes map to an exit code here rather than in the runner, so the one place
    /// that decides is covered for each: a submit that delivered nothing still succeeded, and a
    /// refused one is a failed job rather than bad setup. Built on a stub context, so nothing is
    /// cloned and the write token is never used.</summary>
    [TestMethod]
    public async Task ExecuteAsync_Returns0_WhenThereWasNothingToSubmit()
    {
        var inputDir = Directory.CreateTempSubdirectory("rix-submit-in-").FullName;
        await File.WriteAllTextAsync
        (
            Path.Combine(inputDir, "result.json"),
            """{"status":"success","pendingPrRequests":[],"costUsd":0,"durationSeconds":1}"""
        );

        Assert.AreEqual(0, await Execute(inputDir));
    }

    [TestMethod]
    public async Task ExecuteAsync_Returns1_WhenTheSubmitFailed()
    {
        var inputDir = Directory.CreateTempSubdirectory("rix-submit-in-").FullName;

        Assert.AreEqual(1, await Execute(inputDir));
    }

    private static Task<int> Execute(string inputDir)
    => SubmitCommand.ExecuteAsync
    (
        TestConfig.ValidSubmit(inputDir: inputDir),
        new SubmitContext(new StubGit(), new StubSubmitRepoHost(), _ => { }, new LocalFileSystem()),
        CancellationToken.None
    );
}
