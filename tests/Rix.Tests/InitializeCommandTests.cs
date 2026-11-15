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

    /// <summary>The ref the setup's workflows call.</summary>
    private static WorkflowRef RefOf(InitializeConfig config) => config.Setup switch
    {
        StandaloneSetup standalone => standalone.Ref,
        FactorySetup factory => factory.Ref,
        _ => throw new AssertFailedException($"setup {config.Setup.GetType().Name} has no ref"),
    };

    /// <summary>The workflow a standalone setup's CI-failure caller watches.</summary>
    private static CiWorkflowName CiWorkflowOf(InitializeConfig config) => config.Setup switch
    {
        StandaloneSetup standalone => standalone.CiWorkflow,
        _ => throw new AssertFailedException($"setup {config.Setup.GetType().Name} watches no CI workflow"),
    };

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
        Assert.AreEqual(WorkflowRef.ForThisBuild, RefOf(Read()));
        Assert.AreEqual(WorkflowRef.ForThisBuild, RefOf(Read("--ref", "  ")));
    }

    [TestMethod]
    public void Command_PassesRefFlag_ToConfig()
    {
        Assert.AreEqual("v1.2.3", RefOf(Read("--ref", "v1.2.3")).Value);
    }

    [TestMethod]
    public void Command_DefaultsTheCiWorkflowToCI_WhenFlagAbsentOrBlank()
    {
        Assert.AreEqual(CiWorkflowName.Default, CiWorkflowOf(Read()));
        Assert.AreEqual(CiWorkflowName.Default, CiWorkflowOf(Read("--ci-workflow", "  ")));
    }

    [TestMethod]
    public void Command_PassesCiWorkflowFlag_ToConfig()
    {
        Assert.AreEqual("Build and test", CiWorkflowOf(Read("--ci-workflow", "Build and test")).Value);
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenCiWorkflowCannotBeWritten()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--ci-workflow", "say \"hi\""));

        StringAssert.StartsWith(ex.Message, "--ci-workflow: 'say \"hi\"' is not a workflow name rix can write");
    }

    /// <summary>A factory watches no workflow of its own, so the flag is not one it takes.</summary>
    [TestMethod]
    public void FactorySubcommand_RejectsTheCiWorkflowFlag()
    {
        var parsed = CommandArgs.Parse(InitializeCommand.Build(), "factory", "--ci-workflow", "CI");

        Assert.AreNotEqual(0, parsed.Errors.Count);
    }

    [TestMethod]
    public void Command_SetsUpARepoRunningRixOnItself_WithoutASubcommand()
    {
        Assert.IsInstanceOfType<StandaloneSetup>(Read().Setup);
    }

    [TestMethod]
    public void Command_SetsUpAFactory_ForTheFactorySubcommand()
    {
        Assert.IsInstanceOfType<FactorySetup>(Read("factory").Setup);
    }

    [TestMethod]
    public void FactorySubcommand_TakesDirAndRef_OnEitherSideOfIt()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;

        foreach (var config in new[] { Read("factory", "--dir", dir, "--ref", "v1.2.3"), Read("--dir", dir, "--ref", "v1.2.3", "factory") })
        {
            Assert.IsInstanceOfType<FactorySetup>(config.Setup);
            Assert.AreEqual(Path.GetFullPath(dir), config.TargetDir.Value);
            Assert.AreEqual("v1.2.3", RefOf(config).Value);
        }
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
