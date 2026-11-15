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
    private static RixRef RefOf(InitializeConfig config) => config.Setup switch
    {
        StandaloneSetup standalone => standalone.Ref,
        FactorySetup factory => factory.Ref,
        _ => throw new AssertFailedException($"setup {config.Setup.GetType().Name} has no ref"),
    };

    /// <summary>For the tests about the options every setup shares: a bare <c>rix init</c> names
    /// no factory and is refused, so these pick the setup that needs nothing else.</summary>
    private static InitializeConfig ReadSingleRepo(params string[] args)
    => Read(["--single-repo", .. args], null);

    /// <summary>The workflow whose failures the setup's workflows react to.</summary>
    private static CiWorkflowName CiWorkflowOf(InitializeConfig config) => config.Setup switch
    {
        StandaloneSetup standalone => standalone.CiWorkflow,
        FactoryServedSetup served => served.CiWorkflow,
        _ => throw new AssertFailedException($"setup {config.Setup.GetType().Name} watches no CI workflow"),
    };

    [TestMethod]
    public void Command_PassesDirFlag_ToConfig()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;

        var config = ReadSingleRepo("--dir", dir);

        Assert.AreEqual(Path.GetFullPath(dir), config.TargetDir.Value);
    }

    [TestMethod]
    public void Command_AcceptsTheInitAlias()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        // Parsed by hand: CommandArgs always spells the verb the way the command names it, and the
        // spelling is the point here.
        var parsed = new RootCommand { InitializeCommand.Build() }.Parse(["init", "--single-repo", "--dir", dir]);

        var config = InitializeCommand.CreateConfig(parsed, new LocalFileSystem());

        Assert.AreEqual(Path.GetFullPath(dir), config.TargetDir.Value);
    }

    [TestMethod]
    public void Command_DefaultsDirToCurrentDirectory_WhenFlagAbsent()
    {
        var currentDirectory = Path.GetTempPath();

        var config = Read(["--single-repo"], new StubFileSystem(currentDirectory: currentDirectory));

        Assert.AreEqual(Path.GetFullPath(currentDirectory), config.TargetDir.Value);
    }

    [TestMethod]
    public void Command_DefaultsRefToThisBuildsMajorTag_WhenFlagAbsentOrBlank()
    {
        Assert.AreEqual(RixRef.ForThisBuild, RefOf(ReadSingleRepo()));
        Assert.AreEqual(RixRef.ForThisBuild, RefOf(ReadSingleRepo("--ref", "  ")));
    }

    [TestMethod]
    public void Command_PassesRefFlag_ToConfig()
    {
        Assert.AreEqual("v1.2.3", RefOf(ReadSingleRepo("--ref", "v1.2.3")).Value);
    }

    [TestMethod]
    public void Command_DefaultsTheCiWorkflowToCI_WhenFlagAbsentOrBlank()
    {
        Assert.AreEqual(CiWorkflowName.Default, CiWorkflowOf(ReadSingleRepo()));
        Assert.AreEqual(CiWorkflowName.Default, CiWorkflowOf(ReadSingleRepo("--ci-workflow", "  ")));
    }

    [TestMethod]
    public void Command_PassesCiWorkflowFlag_ToConfig()
    {
        Assert.AreEqual("Build and test", CiWorkflowOf(ReadSingleRepo("--ci-workflow", "Build and test")).Value);
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenCiWorkflowCannotBeWritten()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => ReadSingleRepo("--ci-workflow", "say \"hi\""));

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
    public void Command_SetsUpARepoRunningRixOnItself_ForSingleRepo()
    {
        Assert.IsInstanceOfType<StandaloneSetup>(Read("--single-repo").Setup);
    }

    [TestMethod]
    public void Command_RefusesToGuessTheSetup_WhenNamingNoFactory()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read());

        StringAssert.StartsWith(ex.Message, "name the factory repo that serves this one (rix initialize <owner/name>), pass --single-repo");
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
    public void Command_SetsUpARepoServedByTheNamedFactory()
    {
        var setup = Read("acme/rix-factory").Setup;

        Assert.IsInstanceOfType<FactoryServedSetup>(setup, out var served);
        Assert.AreEqual(new RepoIdentifier("acme/rix-factory"), served.FactoryRepo);
    }

    [TestMethod]
    public void Command_PassesCiWorkflowFlag_ToAServedRepo()
    {
        Assert.AreEqual(CiWorkflowName.Default, CiWorkflowOf(Read("acme/rix-factory")));
        Assert.AreEqual("Build and test", CiWorkflowOf(Read("acme/rix-factory", "--ci-workflow", "Build and test")).Value);
    }

    [TestMethod]
    public void Command_ReportsTheArgument_WhenFactoryRepoIsMalformed()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("rix-factory"));

        StringAssert.StartsWith(ex.Message, "<factory-repo>: 'rix-factory' is not a valid repo identifier");
    }

    [TestMethod]
    public void Command_Rejects_SingleRepoCombinedWithAFactoryRepo()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--single-repo", "acme/rix-factory"));

        StringAssert.StartsWith(ex.Message, "--single-repo and a factory repo cannot be combined");
    }

    /// <summary>Both are the parent's, so they parse ahead of the subcommand: without the check
    /// they would be dropped without a word.</summary>
    [TestMethod]
    [DataRow("acme/rix-factory")]
    [DataRow("--single-repo")]
    public void FactorySubcommand_Rejects_ThePartsOfTheOtherSetups(string other)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read(other, "factory"));

        StringAssert.StartsWith(ex.Message, "factory takes neither a factory repo nor --single-repo");
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenRefIsMalformed()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => ReadSingleRepo("--ref", "main branch"));

        StringAssert.StartsWith(ex.Message, "--ref: 'main branch' is not a valid rix ref");
    }

    [TestMethod]
    public void Command_ReportsTheFlag_WhenDirDoesNotExist()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => ReadSingleRepo("--dir", "/nonexistent/path/xyz"));

        Assert.AreEqual("--dir: directory does not exist: /nonexistent/path/xyz", ex.Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Command_ReportsTheFlag_WhenDirIsBlank(string dir)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => ReadSingleRepo("--dir", dir));

        Assert.AreEqual("--dir is required", ex.Message);
    }

    /// <summary>The failure branch of the one place a run's outcome becomes an exit code: a write
    /// the runner could not perform is reported under <c>error:</c> and exits 2, the same shape a
    /// bad flag gets, because both mean nothing ran.</summary>
    [TestMethod]
    public async Task ExecuteAsync_ReportsTheError_AndReturns2_WhenATemplateCannotBeWritten()
    {
        var log = new StubLog();

        var exitCode = await InitializeCommand.ExecuteAsync
        (
            TestConfig.ValidInitialize(),
            new InitializeContext(new StubFileSystem(writeAllText: (_, _) => throw new IOException("disk full")), log),
            CancellationToken.None
        );

        Assert.AreEqual(2, exitCode);
        var error = log.Lines.Single();
        StringAssert.StartsWith(error, "error: could not write ");
        StringAssert.Contains(error, "disk full");
    }

    /// <summary>Everything an init reports is for the human at the terminal, so none of it is a
    /// result on stdout.</summary>
    [TestMethod]
    public async Task ExecuteAsync_Returns0_AndWritesNoResult_WhenEveryTemplateIsWritten()
    {
        var log = new StubLog();

        var exitCode = await InitializeCommand.ExecuteAsync(TestConfig.ValidInitialize(), new InitializeContext(new StubFileSystem(), log), CancellationToken.None);

        Assert.AreEqual(0, exitCode);
        Assert.IsNotEmpty(log.Lines);
        Assert.IsEmpty(log.Results);
    }
}
