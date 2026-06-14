using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobConfigTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static JobConfigResult Create(
        string repo = "owner/repo",
        string prompt = "Fix the bug",
        string readToken = "read-tok",
        int? maxTokens = null,
        int? timeoutMinutes = null,
        string? workDir = null,
        string? outputDir = null) =>
        JobConfig.Create(repo, prompt, readToken, maxTokens, timeoutMinutes, workDir, outputDir ?? ExistingDir);

    private static JobConfig Valid(JobConfigResult result) => result switch
    {
        JobConfigValid v => v.Config,
        JobConfigInvalid i => throw new AssertFailedException($"expected valid config, got errors: {string.Join("; ", i.Errors)}"),
        _ => throw new AssertFailedException($"unexpected result: {result}"),
    };

    private static IReadOnlyList<string> Errors(JobConfigResult result) => result switch
    {
        JobConfigInvalid i => i.Errors,
        _ => throw new AssertFailedException("expected an invalid config"),
    };

    [TestMethod]
    public void Create_AppliesDefaults()
    {
        var config = Valid(Create());

        Assert.AreEqual(JobConfig.DefaultMaxTokens, config.MaxTokens.Value);
        Assert.AreEqual(JobConfig.DefaultTimeoutMinutes, config.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), config.WorkDir.Value);
    }

    [TestMethod]
    public void Create_OverridesDefaults()
    {
        var config = Valid(Create(maxTokens: 1000, timeoutMinutes: 5, workDir: Path.GetTempPath()));

        Assert.AreEqual(1000, config.MaxTokens.Value);
        Assert.AreEqual(5, config.TimeoutMinutes.Value);
    }

    [TestMethod]
    public void Create_ParsesRepoIntoStrongType()
    {
        var config = Valid(Create(repo: "owner/repo"));
        Assert.AreEqual("owner/repo", config.Repo.ToString());
    }

    [TestMethod]
    public void Create_ReturnsValid_ForValidInputs()
    {
        Assert.IsInstanceOfType<JobConfigValid>(Create());
    }

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void RepoIdentifier_Parse_RejectsInvalidFormat(string repo)
    {
        Assert.IsInstanceOfType<ParseError<RepoIdentifier>>(RepoIdentifier.Parse(repo));
    }

    [TestMethod]
    public void RepoIdentifier_Parse_AcceptsOwnerSlashRepo()
    {
        Assert.IsInstanceOfType<ParseSuccess<RepoIdentifier>>(RepoIdentifier.Parse("owner/repo"));
    }

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void Create_RejectsMalformedRepo(string repo)
    {
        var errors = Errors(Create(repo: repo));
        Assert.IsTrue(errors.Any(e => e.Contains("repo identifier")), $"expected a repo-format error, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void Create_RejectsEmptyRepo()
    {
        Assert.IsTrue(Errors(Create(repo: "")).Any(e => e.Contains("--repo is required")));
    }

    [TestMethod]
    public void Create_RejectsEmptyPrompt()
    {
        Assert.IsTrue(Errors(Create(prompt: "")).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsEmptyReadToken()
    {
        Assert.IsTrue(Errors(Create(readToken: "")).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsNonPositiveMaxTokens()
    {
        Assert.IsTrue(Errors(Create(maxTokens: 0)).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsNonPositiveTimeout()
    {
        Assert.IsTrue(Errors(Create(timeoutMinutes: -1)).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsNonExistentWorkDir()
    {
        Assert.IsTrue(Errors(Create(workDir: "/nonexistent/path/xyz")).Count > 0);
    }

    [TestMethod]
    public void Create_RejectsEmptyOutputDir()
    {
        Assert.IsTrue(Errors(Create(outputDir: "")).Any(e => e.Contains("--output-dir")));
    }

    [TestMethod]
    public void Create_RejectsNonExistentOutputDir()
    {
        Assert.IsTrue(Errors(Create(outputDir: "/nonexistent/out")).Any(e => e.Contains("--output-dir")));
    }

    [TestMethod]
    public void Create_CollectsAllErrors()
    {
        var errors = Errors(Create(repo: "", prompt: "", readToken: ""));
        Assert.IsTrue(errors.Count >= 3, $"expected several errors, got: {string.Join("; ", errors)}");
    }

    [TestMethod]
    public void Create_DefaultsWorkDirToTemp_WhenNullOrWhitespace()
    {
        Assert.AreEqual(Path.GetTempPath(), Valid(Create(workDir: null)).WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), Valid(Create(workDir: "")).WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), Valid(Create(workDir: "   ")).WorkDir.Value);
    }
}
