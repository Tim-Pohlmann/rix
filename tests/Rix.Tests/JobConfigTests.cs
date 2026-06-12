using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobConfigTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static JobConfig ValidConfig() => JobConfig.FromInputs(
        repo: "owner/repo",
        prompt: "Fix the bug",
        readToken: "read-tok",
        options: new JobInputOptions(OutputDir: ExistingDir));

    [TestMethod]
    public void FromInputs_AppliesDefaults()
    {
        var config = ValidConfig();

        Assert.AreEqual(JobConfig.DefaultMaxTokens, config.MaxTokens.Value);
        Assert.AreEqual(JobConfig.DefaultTimeoutMinutes, config.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), config.WorkDir);
    }

    [TestMethod]
    public void FromInputs_OverridesDefaults()
    {
        var config = JobConfig.FromInputs(
            repo: "owner/repo",
            prompt: "Fix",
            readToken: "r",
            options: new JobInputOptions(MaxTokens: 1000, TimeoutMinutes: 5, WorkDir: Path.GetTempPath(), OutputDir: ExistingDir));

        Assert.AreEqual(1000, config.MaxTokens.Value);
        Assert.AreEqual(5, config.TimeoutMinutes.Value);
    }

    [TestMethod]
    public void ValidationErrors_ReturnsNoErrors_ForValidConfig()
    {
        var errors = ValidConfig().ValidationErrors;
        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void RepoIdentifier_ThrowsOnInvalidFormat(string repo)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new RepoIdentifier(repo));
    }

    [TestMethod]
    public void ValidationErrors_RejectsEmptyPrompt()
    {
        var errors = (ValidConfig() with { Prompt = "" }).ValidationErrors;
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void ValidationErrors_RejectsEmptyReadToken()
    {
        var errors = (ValidConfig() with { ReadToken = new ReadToken("") }).ValidationErrors;
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void ValidationErrors_RejectsNonPositiveMaxTokens()
    {
        var errors = (ValidConfig() with { MaxTokens = new MaxTokens(0) }).ValidationErrors;
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void ValidationErrors_RejectsNonPositiveTimeout()
    {
        var errors = (ValidConfig() with { TimeoutMinutes = new TimeoutMinutes(-1) }).ValidationErrors;
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void ValidationErrors_RejectsNonExistentWorkDir()
    {
        var errors = (ValidConfig() with { WorkDir = "/nonexistent/path/xyz" }).ValidationErrors;
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void ValidationErrors_RejectsEmptyWorkDir()
    {
        var errors = (ValidConfig() with { WorkDir = "" }).ValidationErrors;
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void ValidationErrors_RejectsEmptyOutputDir()
    {
        var errors = (ValidConfig() with { OutputDir = "" }).ValidationErrors;
        Assert.IsTrue(errors.Any(e => e.Contains("--output-dir")));
    }

    [TestMethod]
    public void ValidationErrors_RejectsNonExistentOutputDir()
    {
        var errors = (ValidConfig() with { OutputDir = "/nonexistent/out" }).ValidationErrors;
        Assert.IsTrue(errors.Any(e => e.Contains("--output-dir")));
    }

    [TestMethod]
    public void FromInputs_DefaultsWorkDirToTemp_WhenNullOrWhitespace()
    {
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", new JobInputOptions(WorkDir: null, OutputDir: ExistingDir)).WorkDir);
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", new JobInputOptions(WorkDir: "", OutputDir: ExistingDir)).WorkDir);
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", new JobInputOptions(WorkDir: "   ", OutputDir: ExistingDir)).WorkDir);
    }
}
