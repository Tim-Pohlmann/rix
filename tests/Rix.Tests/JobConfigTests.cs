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

        Assert.AreEqual(JobConfig.DefaultMaxTokens, config.MaxTokens);
        Assert.AreEqual(JobConfig.DefaultTimeoutMinutes, config.TimeoutMinutes);
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

        Assert.AreEqual(1000, config.MaxTokens);
        Assert.AreEqual(5, config.TimeoutMinutes);
    }

    [TestMethod]
    public void Validate_ReturnsNoErrors_ForValidConfig()
    {
        var errors = ValidConfig().Validate();
        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    [DataRow("", "owner/repo is required")]
    [DataRow("noslash", "owner/repo must contain a slash")]
    public void Validate_RejectsInvalidRepo(string repo, string _)
    {
        var config = ValidConfig() with { Repo = repo };
        var errors = config.Validate();
        Assert.IsTrue(errors.Count > 0, "Expected validation errors for repo: " + repo);
    }

    [TestMethod]
    public void Validate_RejectsEmptyPrompt()
    {
        var errors = (ValidConfig() with { Prompt = "" }).Validate();
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void Validate_RejectsEmptyReadToken()
    {
        var errors = (ValidConfig() with { ReadToken = "" }).Validate();
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void Validate_RejectsEmptyWriteToken()
    {
        var errors = (ValidConfig() with { WriteToken = "" }).Validate();
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void Validate_RejectsNonPositiveMaxTokens()
    {
        var errors = (ValidConfig() with { MaxTokens = 0 }).Validate();
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void Validate_RejectsNonPositiveTimeout()
    {
        var errors = (ValidConfig() with { TimeoutMinutes = -1 }).Validate();
        Assert.IsTrue(errors.Count > 0);
    }

    [TestMethod]
    public void Validate_RejectsNonExistentWorkDir()
    {
        var errors = (ValidConfig() with { WorkDir = "/nonexistent/path/xyz" }).Validate();
        Assert.IsTrue(errors.Count > 0);
    }
}
