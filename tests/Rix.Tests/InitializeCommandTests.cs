using Rix.Cli;
using Rix.Initialize;
using System.CommandLine;

namespace Rix.Tests;

[TestClass]
public class InitializeCommandTests
{
    private static InitializeConfig Read(string[] args, IFileSystem? fileSystem = null)
    => InitializeCommand.CreateConfig(CommandArgs.Parse(InitializeCommand.Build(), args), fileSystem ?? new LocalFileSystem());

    private static InitializeConfig Read(params string[] args)
    => Read(args, null);

    [TestMethod]
    public void Command_PassesDirFlag_ToConfig()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;

        var config = Read("--dir", dir);

        Assert.AreEqual(Path.GetFullPath(dir), config.TargetDir.Value);
    }

    [TestMethod]
    public void Command_AcceptsTheInitAlias()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        // Parsed by hand: CommandArgs always spells the verb the way the command names it, and the
        // spelling is the point here.
        var parsed = new RootCommand { InitializeCommand.Build() }.Parse(["init", "--dir", dir]);

        var config = InitializeCommand.CreateConfig(parsed, new LocalFileSystem());

        Assert.AreEqual(Path.GetFullPath(dir), config.TargetDir.Value);
    }

    [TestMethod]
    public void Command_DefaultsDirToCurrentDirectory_WhenFlagAbsent()
    {
        var currentDirectory = Path.GetTempPath();

        var config = Read([], new StubFileSystem(currentDirectory: currentDirectory));

        Assert.AreEqual(Path.GetFullPath(currentDirectory), config.TargetDir.Value);
    }

    [TestMethod]
    public void Command_DefaultsRefToThisBuildsMajorTag_WhenFlagAbsentOrBlank()
    {
        Assert.AreEqual(WorkflowRef.ForThisBuild, Read().Ref);
        Assert.AreEqual(WorkflowRef.ForThisBuild, Read("--ref", "  ").Ref);
    }

    [TestMethod]
    public void Command_PassesRefFlag_ToConfig()
    {
        Assert.AreEqual("v1.2.3", Read("--ref", "v1.2.3").Ref.Value);
    }

    [TestMethod]
    public void Command_SetsUpARepoRunningRixOnItself_WhenFactoryFlagAbsent()
    {
        Assert.IsInstanceOfType<StandaloneSetup>(Read().Setup);
    }

    [TestMethod]
    public void Command_SetsUpAFactory_WhenFactoryFlagPresent()
    {
        Assert.IsInstanceOfType<FactorySetup>(Read("--factory").Setup);
    }

    [TestMethod]
    public void Command_SetsUpARepoServedByTheNamedFactory_WhenFactoryRepoFlagPresent()
    {
        var setup = Read("--factory-repo", "acme/rix-factory").Setup;

        Assert.IsInstanceOfType<FactoryServedSetup>(setup, out var served);
        Assert.AreEqual(new RepoIdentifier("acme/rix-factory"), served.FactoryRepo);
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenFactoryRepoIsMalformed()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--factory-repo", "rix-factory"));

        StringAssert.StartsWith(ex.Message, "--factory-repo: 'rix-factory' is not a valid repo identifier");
    }

    [TestMethod]
    public void Command_Rejects_FactoryCombinedWithFactoryRepo()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--factory", "--factory-repo", "acme/rix-factory"));

        StringAssert.StartsWith(ex.Message, "--factory and --factory-repo cannot be combined");
    }

    [TestMethod]
    public void NextSteps_ScopeTheDispatchTokenToTheNamedFactory()
    {
        var steps = InitializeCommand.NextSteps(new FactoryServedSetup(new("acme/rix-factory")));

        StringAssert.Contains(steps, "RIX_FACTORY_DISPATCH_TOKEN   PAT, contents:write on acme/rix-factory only");
        StringAssert.Contains(steps, "In notify-rix-factory.yml, set workflows: [\"CI\"]");
    }

    [TestMethod]
    public void NextSteps_PointAFactoryAtTheReposItServes()
    {
        var steps = InitializeCommand.NextSteps(new FactorySetup());

        StringAssert.Contains(steps, "on every repo this factory serves");
        StringAssert.Contains(steps, "rix initialize --factory-repo <this repo's owner/name>");
        // A factory answers dispatched failures, so there is no watched workflow to rename.
        Assert.IsFalse(steps.Contains("workflows: [\"CI\"]", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenRefIsMalformed()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--ref", "main branch"));

        StringAssert.StartsWith(ex.Message, "--ref: 'main branch' is not a valid workflow ref");
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenDirDoesNotExist()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--dir", "/nonexistent/path/xyz"));

        Assert.AreEqual("--dir: directory does not exist: /nonexistent/path/xyz", ex.Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Command_ReportsTheFlag_WhenDirIsBlank(string dir)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--dir", dir));

        Assert.AreEqual("--dir is required", ex.Message);
    }

    /// <summary>The failure branch of the one place a run's outcome becomes an exit code: a write
    /// the runner could not perform is reported under <c>error:</c> and exits 2, the same shape a
    /// bad flag gets, because both mean nothing ran.</summary>
    [TestMethod]
    public async Task ExecuteAsync_ReportsTheError_AndReturns2_WhenATemplateCannotBeWritten()
    {
        using var stderr = new ConsoleErrorScope();

        var exitCode = await InitializeCommand.ExecuteAsync
        (
            TestConfig.ValidInitialize(),
            new InitializeContext(new StubFileSystem(writeAllText: (_, _) => throw new IOException("disk full")), new StubLog()),
            CancellationToken.None
        );

        Assert.AreEqual(2, exitCode);
        StringAssert.Contains(stderr.Text, "error: could not write ");
        StringAssert.Contains(stderr.Text, "disk full");
    }
}
