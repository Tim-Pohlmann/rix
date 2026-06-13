using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class GitEnvironmentTests
{
    [TestMethod]
    public void Current_ExposesInheritedPathAndHome()
    {
        var env = GitEnvironment.Current;

        Assert.AreEqual(Environment.GetEnvironmentVariable("PATH") ?? "", env["PATH"]);
        Assert.AreEqual(Environment.GetEnvironmentVariable("HOME") ?? "", env["HOME"]);
    }
}
