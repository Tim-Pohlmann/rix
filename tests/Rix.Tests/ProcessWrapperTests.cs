using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class ProcessWrapperTests
{
    private static string Shell => OperatingSystem.IsWindows() switch { true => "pwsh", false => "/bin/sh" };

    private static string Echo(string text)
    => OperatingSystem.IsWindows() switch { true => $"Write-Output '{text}'", false => $"echo {text}" };

    private static string SleepSeconds(int n)
    => OperatingSystem.IsWindows() switch { true => $"Start-Sleep -s {n}", false => $"sleep {n}" };

    private static string PrintEnvOrFallback(string varName, string fallback)
    => OperatingSystem.IsWindows() switch
    {
        true => $"$v=$env:{varName}; if($v){{$v}}else{{'{fallback}'}}",
        false => $"echo ${{{varName}:-{fallback}}}",
    };

    [TestMethod]
    public async Task RunAsync_CapturesStdoutLines()
    {
        var lines = new List<string>();

        var result = await ProcessWrapper.RunAsync(
            Shell, ["-c", Echo("hello")],
            workingDirectory: Path.GetTempPath(),
            onStdoutLine: lines.Add,
            cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<ProcessSuccess>(result);
        Assert.IsTrue(lines.Any(l => l.Contains("hello")), $"Expected 'hello' in output. Got: [{string.Join(", ", lines)}]");
    }

    [TestMethod]
    public async Task RunAsync_Output_IsLastNonEmptyStdoutLine()
    {
        var command = OperatingSystem.IsWindows()
            ? "Write-Output 'first'; Write-Output 'last'"
            : "echo first; echo last";

        var result = await ProcessWrapper.RunAsync(
            Shell, ["-c", command],
            workingDirectory: Path.GetTempPath(),
            cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<ProcessSuccess>(result);
        Assert.AreEqual("last", ((ProcessSuccess)result).Output);
    }

    [TestMethod]
    public async Task RunAsync_Output_IsNull_WhenNoStdout()
    {
        var result = await ProcessWrapper.RunAsync(
            Shell, ["-c", "exit 0"],
            workingDirectory: Path.GetTempPath(),
            cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<ProcessSuccess>(result);
        Assert.IsNull(((ProcessSuccess)result).Output);
    }

    [TestMethod]
    public async Task RunAsync_ReportsNonZeroExitCode()
    {
        var result = await ProcessWrapper.RunAsync(
            Shell, ["-c", "exit 1"],
            workingDirectory: Path.GetTempPath(),
            cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        Assert.AreEqual("exited with code 1", ((ProcessFailure)result).Reason);
    }

    [TestMethod]
    public async Task RunAsync_PropagatesOnStdoutLineException()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ProcessWrapper.RunAsync(
                Shell, ["-c", Echo("hello")],
                workingDirectory: Path.GetTempPath(),
                onStdoutLine: _ => throw new InvalidOperationException("callback failed"),
                cancellationToken: CancellationToken.None));
    }

    [TestMethod]
    public async Task RunAsync_TimesOut_WhenCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await ProcessWrapper.RunAsync(
            Shell, ["-c", SleepSeconds(60)],
            workingDirectory: Path.GetTempPath(),
            cancellationToken: cts.Token);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        Assert.AreEqual("timed out", ((ProcessFailure)result).Reason);
    }

    [TestMethod]
    public async Task RunAsync_ReturnsFailure_WhenExecutableNotFound()
    {
        var result = await ProcessWrapper.RunAsync(
            "rix-no-such-executable", [],
            workingDirectory: Path.GetTempPath(),
            cancellationToken: CancellationToken.None);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        StringAssert.Contains(((ProcessFailure)result).Reason, "rix-no-such-executable");
    }

    [TestMethod]
    public async Task RunAsync_InheritsParentEnv_WhenNoOverrides()
    {
        using var scope = new EnvScope();
        scope.Set("RIX_TEST_INHERIT", "inherited-value");

        var lines = new List<string>();
        await ProcessWrapper.RunAsync(
            Shell, ["-c", PrintEnvOrFallback("RIX_TEST_INHERIT", "ABSENT")],
            workingDirectory: Path.GetTempPath(),
            onStdoutLine: lines.Add,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("inherited-value")), "Child should inherit parent env");
    }

    [TestMethod]
    public async Task RunAsync_AppliesOverridesOnTopOfParentEnv_WhenProvided()
    {
        using var scope = new EnvScope();
        scope.Set("RIX_TEST_INHERIT", "inherited-value");
        scope.Set("RIX_TEST_OVERRIDE", "original-value");

        var overrides = new Dictionary<string, string> { ["RIX_TEST_OVERRIDE"] = "overridden-value" };

        var inheritLines = new List<string>();
        await ProcessWrapper.RunAsync(
            Shell, ["-c", PrintEnvOrFallback("RIX_TEST_INHERIT", "ABSENT")],
            workingDirectory: Path.GetTempPath(),
            environmentOverrides: overrides,
            onStdoutLine: inheritLines.Add,
            cancellationToken: CancellationToken.None);

        var overrideLines = new List<string>();
        await ProcessWrapper.RunAsync(
            Shell, ["-c", PrintEnvOrFallback("RIX_TEST_OVERRIDE", "ABSENT")],
            workingDirectory: Path.GetTempPath(),
            environmentOverrides: overrides,
            onStdoutLine: overrideLines.Add,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(inheritLines.Any(l => l.Contains("inherited-value")), "Child should still inherit parent env when overrides provided");
        Assert.IsTrue(overrideLines.Any(l => l.Contains("overridden-value")), "Override should replace the original value");
    }
}
