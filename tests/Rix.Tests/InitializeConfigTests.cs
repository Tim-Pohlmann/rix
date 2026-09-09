using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class InitializeConfigTests
{
    private static InitializeConfig Valid(InitializeConfigResult result) => result switch
    {
        InitializeConfigValid v => v.Config,
        InitializeConfigInvalid i => throw new AssertFailedException($"expected valid config, got errors: {string.Join("; ", i.Errors)}"),
        _ => throw new AssertFailedException($"unexpected result: {result}"),
    };

    private static IReadOnlyList<string> Errors(InitializeConfigResult result) => result switch
    {
        InitializeConfigInvalid i => i.Errors,
        _ => throw new AssertFailedException("expected an invalid config"),
    };

    [TestMethod]
    public void Create_ReturnsValid_ForAnExistingDirectory()
    {
        var config = Valid(InitializeConfig.Create(Path.GetTempPath()));
        Assert.AreEqual(Path.GetFullPath(Path.GetTempPath()), config.TargetDir.Value);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_RejectsBlankDir(string dir)
    {
        Assert.IsTrue(Errors(InitializeConfig.Create(dir)).Any(e => e.Contains("--dir")));
    }

    [TestMethod]
    public void Create_RejectsNonExistentDir()
    {
        Assert.IsTrue(Errors(InitializeConfig.Create("/nonexistent/path/xyz")).Any(e => e.Contains("--dir")));
    }
}
