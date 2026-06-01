using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobConfigTests
{
    private static JobConfig ValidConfig() => JobConfig.FromInputs(
        repo: "owner/repo",
        prompt: "Fix the bug",
        readToken: "read-tok",
        writeToken: "write-tok",
        maxTokens: null,
        timeoutMinutes: null,
        workDir: null);

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
            writeToken: "w",
            maxTokens: 1000,
            timeoutMinutes: 5,
            workDir: Path.GetTempPath());

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
    public void ValidationErrors_RejectsEmptyRepo()
    {
        var config = ValidConfig() with { Repo = new RepoIdentifier("") };
        Assert.IsTrue(config.ValidationErrors.Contains("--repo is required"));
    }

    [TestMethod]
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
    public void ValidationErrors_RejectsEmptyWriteToken()
    {
        var errors = (ValidConfig() with { WriteToken = new WriteToken("") }).ValidationErrors;
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
    public void FromInputs_DefaultsWorkDirToTemp_WhenNullOrWhitespace()
    {
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", "w", null, null, null).WorkDir);
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", "w", null, null, "").WorkDir);
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", "w", null, null, "   ").WorkDir);
    }
}
