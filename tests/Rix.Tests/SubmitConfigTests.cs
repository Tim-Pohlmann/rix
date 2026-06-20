using Rix.Submit;

namespace Rix.Tests;

[TestClass]
public class SubmitConfigTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static SubmitConfigResult Create(
        string repo = "owner/repo",
        string writeToken = "write-tok",
        string? inputDir = null,
        string? workDir = null) =>
        SubmitConfig.Create(repo, writeToken, inputDir ?? ExistingDir, workDir);

    private static SubmitConfig Valid(SubmitConfigResult result) => result switch
    {
        SubmitConfigValid v => v.Config,
        SubmitConfigInvalid i => throw new AssertFailedException($"expected valid config, got errors: {string.Join("; ", i.Errors)}"),
        _ => throw new AssertFailedException($"unexpected result: {result}"),
    };

    private static IReadOnlyList<string> Errors(SubmitConfigResult result) => result switch
    {
        SubmitConfigInvalid i => i.Errors,
        _ => throw new AssertFailedException("expected an invalid config"),
    };

    [TestMethod]
    public void Create_ReturnsValid_ForValidInputs()
    {
        var config = Valid(Create());
        Assert.AreEqual("owner/repo", config.Repo.ToString());
        Assert.AreEqual("write-tok", config.WriteToken.Value);
    }

    [TestMethod]
    public void Create_DefaultsWorkDirToTemp_WhenNullOrWhitespace()
    {
        Assert.AreEqual(Path.GetTempPath(), Valid(Create(workDir: null)).WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), Valid(Create(workDir: "   ")).WorkDir.Value);
    }

    [TestMethod]
    public void Create_RejectsEmptyRepo()
    {
        Assert.IsTrue(Errors(Create(repo: "")).Any(e => e.Contains("--repo is required")));
    }

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    public void Create_RejectsMalformedRepo(string repo)
    {
        Assert.IsTrue(Errors(Create(repo: repo)).Any(e => e.Contains("repo identifier")));
    }

    [TestMethod]
    public void Create_RejectsEmptyWriteToken()
    {
        Assert.IsTrue(Errors(Create(writeToken: "")).Any(e => e.Contains("--write-token")));
    }

    [TestMethod]
    public void Create_RejectsEmptyInputDir()
    {
        Assert.IsTrue(Errors(Create(inputDir: "")).Any(e => e.Contains("--input-dir")));
    }

    [TestMethod]
    public void Create_RejectsNonExistentInputDir()
    {
        Assert.IsTrue(Errors(Create(inputDir: "/nonexistent/in")).Any(e => e.Contains("--input-dir")));
    }

    [TestMethod]
    public void Create_RejectsNonExistentWorkDir()
    {
        Assert.IsTrue(Errors(Create(workDir: "/nonexistent/path/xyz")).Count > 0);
    }

    [TestMethod]
    public void Create_CollectsAllErrors()
    {
        var errors = Errors(Create(repo: "", writeToken: "", inputDir: ""));
        Assert.IsTrue(errors.Count >= 3, $"expected several errors, got: {string.Join("; ", errors)}");
    }
}
