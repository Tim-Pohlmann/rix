using Rix.Cli;
using Rix.Initialize;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class InitializeCommandTests
{
    private static Parser BuildParser(Func<InitializeConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(InitializeCommand.Build(handler));
        return new CommandLineBuilder(root).UseDefaults().Build();
    }

    [TestMethod]
    public async Task Command_PassesDirFlag_ToConfig()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        InitializeConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(["initialize", "--dir", dir]);

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
    public async Task Command_IsReachableViaInitAlias()
    {
        var dir = Directory.CreateTempSubdirectory("rix-init-").FullName;
        InitializeConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(["init", "--dir", dir]);

        Assert.IsNotNull(captured);
        Assert.AreEqual(Path.GetFullPath(dir), captured.TargetDir.Value);
    }

    [TestMethod]
    public async Task Command_Returns2_WhenDirDoesNotExist()
    {
        var parser = BuildParser(_ => Task.FromResult(0));
        var exitCode = await parser.InvokeAsync(["initialize", "--dir", "/nonexistent/path/xyz"]);
        Assert.AreEqual(2, exitCode);
    }
}
