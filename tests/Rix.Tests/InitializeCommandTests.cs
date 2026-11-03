using Rix.Cli;
using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class InitializeCommandTests
{
    private static InitializeConfig Read(params string[] args)
    => InitializeCommand.ReadConfig(CommandArgs.Parse(InitializeCommand.Build(), args));

    [TestMethod]
    [DataRow("initialize")]
    [DataRow("init")]
    public void Command_PassesDirFlag_ToConfig(string verb)
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;

        var config = Read(verb, "--dir", dir);

        Assert.AreEqual(Path.GetFullPath(dir), config.TargetDir.Value);
    }

    [TestMethod]
    public void Command_DefaultsDirToCurrentDirectory_WhenFlagAbsent()
    {
        var config = Read("initialize");

        Assert.AreEqual(Path.GetFullPath("."), config.TargetDir.Value);
    }

    [TestMethod]
    public void Command_DefaultsRefToThisBuildsMajorTag_WhenFlagAbsentOrBlank()
    {
        Assert.AreEqual(WorkflowRef.ForThisBuild, Read("initialize").Ref);
        Assert.AreEqual(WorkflowRef.ForThisBuild, Read("initialize", "--ref", "  ").Ref);
    }

    [TestMethod]
    public void Command_PassesRefFlag_ToConfig()
    {
        Assert.AreEqual("v1.2.3", Read("initialize", "--ref", "v1.2.3").Ref.Value);
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenRefIsMalformed()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("initialize", "--ref", "main branch"));

        StringAssert.StartsWith(ex.Message, "--ref: 'main branch' is not a valid workflow ref");
    }

    [TestMethod]
    public void RequiredDirectories_NamesTheDirAfterItsFlag()
    {
        var config = Read("initialize", "--dir", "/nonexistent/path/xyz");

        Assert.AreEqual(new RequiredDirectory("--dir", new DirectoryPath("/nonexistent/path/xyz")), InitializeCommand.RequiredDirectories(config).Single());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Command_ReportsTheFlag_WhenDirIsBlank(string dir)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("initialize", "--dir", dir));

        Assert.AreEqual("--dir is required", ex.Message);
    }
}
