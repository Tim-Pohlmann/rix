using Rix.Cli;
using Rix.Initialize;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class InitializeCommandTests
{
    private static Parser BuildParser(Func<InitializeConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(InitializeCommand.Build(handler));
        return CliPipeline.Build(root);
    }

    [TestMethod]
    [DataRow("initialize")]
    [DataRow("init")]
    public async Task Command_PassesDirFlag_ToConfig(string verb)
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        InitializeConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync([verb, "--dir", dir]);

        Assert.IsNotNull(captured);
        Assert.AreEqual(Path.GetFullPath(dir), captured.TargetDir.Value);
    }

    [TestMethod]
    public async Task Command_DefaultsDirToCurrentDirectory_WhenFlagAbsent()
    {
        InitializeConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync("initialize");

        Assert.IsNotNull(captured);
        Assert.AreEqual(Path.GetFullPath(Directory.GetCurrentDirectory()), captured.TargetDir.Value);
    }

    [TestMethod]
    public async Task Command_Returns2_WhenDirDoesNotExist()
    {
        InitializeConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(["initialize", "--dir", "/nonexistent/path/xyz"]);

        Assert.IsNull(captured);
        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, "error: --dir: directory does not exist: /nonexistent/path/xyz");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task Command_Returns2_WhenDirIsBlank(string dir)
    {
        var parser = BuildParser(_ => Task.FromResult(0));

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(["initialize", "--dir", dir]);

        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, "error: --dir is required");
    }
}
