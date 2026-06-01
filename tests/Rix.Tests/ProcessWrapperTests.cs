using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class ProcessWrapperTests
{
    [TestMethod]
    public async Task RunAsync_CapturesStdoutLines()
    {
        var lines = new List<string>();

        var result = await ProcessWrapper.RunAsync(
            "/bin/sh", ["-c", "echo hello"],
            workingDirectory: Path.GetTempPath(),
            onStdoutLine: lines.Add,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(lines.Any(l => l.Contains("hello")), $"Expected 'hello' in output. Got: [{string.Join(", ", lines)}]");
    }

    [TestMethod]
    public async Task RunAsync_ReportsNonZeroExitCode()
    {
        var result = await ProcessWrapper.RunAsync(
            "/bin/sh", ["-c", "exit 1"],
            workingDirectory: Path.GetTempPath(),
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, result.ExitCode);
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task RunAsync_PropagatesOnStdoutLineException()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ProcessWrapper.RunAsync(
                "/bin/sh", ["-c", "echo hello"],
                workingDirectory: Path.GetTempPath(),
                onStdoutLine: _ => throw new InvalidOperationException("callback failed"),
                cancellationToken: CancellationToken.None));
    }

    [TestMethod]
    public async Task RunAsync_TimesOut_WhenCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await ProcessWrapper.RunAsync(
            "/bin/sh", ["-c", "sleep 60"],
            workingDirectory: Path.GetTempPath(),
            cancellationToken: cts.Token);

        Assert.IsTrue(result.TimedOut);
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task RunAsync_InheritsParentEnv_WhenNoOverrides()
    {
        using var scope = new EnvScope();
        scope.Set("RIX_TEST_INHERIT", "inherited-value");

        var lines = new List<string>();
        await ProcessWrapper.RunAsync(
            "/bin/sh", ["-c", "echo ${RIX_TEST_INHERIT:-ABSENT}"],
            workingDirectory: Path.GetTempPath(),
            onStdoutLine: lines.Add,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("inherited-value")), "Child should inherit parent env");
    }

    [TestMethod]
    public async Task RunAsync_UsesOnlyOverrides_WhenProvided()
    {
        using var scope = new EnvScope();
        scope.Set("RIX_TEST_INHERIT", "inherited-value");

        var lines = new List<string>();
        await ProcessWrapper.RunAsync(
            "/bin/sh", ["-c", "echo ${RIX_TEST_INHERIT:-ABSENT}"],
            workingDirectory: Path.GetTempPath(),
            environmentOverrides: new Dictionary<string, string> { ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "" },
            onStdoutLine: lines.Add,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(lines.Any(l => l.Contains("ABSENT")), "Child should not see parent env when overrides provided");
    }
}
