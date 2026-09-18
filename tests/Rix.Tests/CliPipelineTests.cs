using Rix.Cli;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class CliPipelineTests
{
    private static Parser BuildParser(Func<Task> handler)
    {
        var command = new Command("boom");
        command.SetHandler(async _ => await handler());
        var root = new RootCommand();
        root.AddCommand(command);
        return CliPipeline.Build(root);
    }

    [TestMethod]
    public async Task InvalidInput_IsReportedOnStderr_WithSetupFailedExitCode()
    {
        var parser = BuildParser(() => throw new InvalidInputException("--repo is required"));

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync("boom");

        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        Assert.AreEqual("error: --repo is required", stderr.Text.Trim());
    }

    [TestMethod]
    public async Task OtherExceptions_FallThroughToTheStockHandler()
    {
        var parser = BuildParser(() => throw new InvalidOperationException("not caller input"));

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync("boom");

        // The stock System.CommandLine handler: reports the crash and exits non-zero without
        // claiming it was a setup problem the caller can fix.
        Assert.AreNotEqual(ExitCodes.Success, exitCode);
        Assert.AreNotEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, "not caller input");
    }
}
