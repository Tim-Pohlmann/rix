using Rix.Cli;
using System.CommandLine;

namespace Rix.Tests;

[TestClass]
public class CliPipelineTests
{
    private static Task<int> InvokeAsync(Func<Task<int>> action)
    {
        var command = new Command("boom");
        command.SetAction(CliPipeline.ReportingInvalidInput(_ => action()));
        return CliPipeline.InvokeAsync(new RootCommand { command }, ["boom"]);
    }

    [TestMethod]
    public async Task InvalidInput_IsReportedOnStderr_WithSetupFailedExitCode()
    {
        using var stderr = new ConsoleErrorScope();
        var exitCode = await InvokeAsync(() => throw new InvalidInputException("--repo is required"));

        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        Assert.AreEqual("error: --repo is required", stderr.Text.Trim());
    }

    [TestMethod]
    public async Task OtherExceptions_FallThroughToTheStockHandler()
    {
        using var stderr = new ConsoleErrorScope();
        var exitCode = await InvokeAsync(() => throw new InvalidOperationException("not caller input"));

        // The stock System.CommandLine handler: reports the crash and exits non-zero without
        // claiming it was a setup problem the caller can fix.
        Assert.AreNotEqual(ExitCodes.Success, exitCode);
        Assert.AreNotEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, "not caller input");
    }

    [TestMethod]
    public async Task ActionExitCode_IsReturned()
    {
        Assert.AreEqual(42, await InvokeAsync(() => Task.FromResult(42)));
    }
}
