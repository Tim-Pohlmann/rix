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
        maxTokens: null,
        timeoutMinutes: null,
        workDir: null,
        outputDir: ExistingDir);

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
            maxTokens: 1000,
            timeoutMinutes: 5,
            workDir: Path.GetTempPath(),
            outputDir: ExistingDir);

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
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void ValidationErrors_RejectsMalformedRepo(string repo)
    {
        var errors = (ValidConfig() with { Repo = repo }).ValidationErrors;
        Assert.IsTrue(errors.Any(e => e.Contains("repo identifier")), $"expected a repo-format error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void ValidationErrors_RejectsEmptyRepo()
    {
        var errors = (ValidConfig() with { Repo = "" }).ValidationErrors;
        Assert.IsTrue(errors.Any(e => e.Contains("--repo is required")));
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
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", null, null, null, ExistingDir).WorkDir);
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", null, null, "", ExistingDir).WorkDir);
        Assert.AreEqual(Path.GetTempPath(), JobConfig.FromInputs("o/r", "p", "r", null, null, "   ", ExistingDir).WorkDir);
    }
}
