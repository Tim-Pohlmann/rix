using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class ProcessWrapperTests
{
    [TestMethod]
    public void BuildSanitizedEnvironment_ExcludesNonAllowedVars()
    {
        using var env = new EnvScope();
        env.Set("RIX_WRITE_TOKEN", "secret");
        Assert.IsFalse(ProcessWrapper.BuildSanitizedEnvironment().ContainsKey("RIX_WRITE_TOKEN"));
    }

    [TestMethod]
    public void BuildSanitizedEnvironment_ReturnsMutableDictionary()
    {
        var env = ProcessWrapper.BuildSanitizedEnvironment();
        env["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = "42000";
        Assert.AreEqual("42000", env["CLAUDE_CODE_MAX_OUTPUT_TOKENS"]);
    }

    [TestMethod]
    public async Task RunAsync_CapturesStdoutLines()
    {
        var lines = new List<string>();
        var env = ProcessWrapper.BuildSanitizedEnvironment();

        var (fileName, args) = EchoCommand("hello");
        var result = await ProcessWrapper.RunAsync(
            fileName, args,
            workingDirectory: Path.GetTempPath(),
            environment: env,
            onStdoutLine: lines.Add,
            cancellationToken: CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(lines.Any(l => l.Contains("hello")), $"Expected 'hello' in output. Got: [{string.Join(", ", lines)}]");
    }

    [TestMethod]
    public async Task RunAsync_ReportsNonZeroExitCode()
    {
        var env = ProcessWrapper.BuildSanitizedEnvironment();
        var (fileName, args) = ExitCommand(1);

        var result = await ProcessWrapper.RunAsync(
            fileName, args,
            workingDirectory: Path.GetTempPath(),
            environment: env,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, result.ExitCode);
        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task RunAsync_PropagatesOnStdoutLineException()
    {
        var env = ProcessWrapper.BuildSanitizedEnvironment();
        var (fileName, args) = EchoCommand("hello");

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => ProcessWrapper.RunAsync(
                fileName, args,
                workingDirectory: Path.GetTempPath(),
                environment: env,
                onStdoutLine: _ => throw new InvalidOperationException("callback failed"),
                cancellationToken: CancellationToken.None));
    }

    [TestMethod]
    public async Task RunAsync_TimesOut_WhenCancelled()
    {
        var env = ProcessWrapper.BuildSanitizedEnvironment();
        var (fileName, args) = SleepCommand(60);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await ProcessWrapper.RunAsync(
            fileName, args,
            workingDirectory: Path.GetTempPath(),
            environment: env,
            cancellationToken: cts.Token);

        Assert.IsTrue(result.TimedOut);
        Assert.IsFalse(result.Succeeded);
    }

    private static (string fileName, string[] args) EchoCommand(string text)
    {
        if (OperatingSystem.IsWindows())
            return ("cmd.exe", ["/c", $"echo {text}"]);
        return ("/bin/sh", ["-c", $"echo {text}"]);
    }

    private static (string fileName, string[] args) ExitCommand(int code)
    {
        if (OperatingSystem.IsWindows())
            return ("cmd.exe", ["/c", $"exit {code}"]);
        return ("/bin/sh", ["-c", $"exit {code}"]);
    }

    private static (string fileName, string[] args) SleepCommand(int seconds)
    {
        if (OperatingSystem.IsWindows())
            return ("cmd.exe", ["/c", $"ping 127.0.0.1 -n {seconds + 1}"]);
        return ("/bin/sh", ["-c", $"sleep {seconds}"]);
    }
}
