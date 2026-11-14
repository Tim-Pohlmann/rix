using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class ProcessWrapperTests
{
    private static readonly ProcessWrapper Wrapper = new();

    // RunAsync takes both line sinks on every call - stderr has no optional reader, see
    // IProcessRunner - while most tests here care about neither, so this supplies the ones a given
    // test doesn't look at. The working directory is the same temp directory throughout.
    private static Task<ProcessResult> RunAsync
    (
        string fileName,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        Action<string>? reportOutput = null,
        Action<string>? reportDiagnostic = null,
        CancellationToken cancellationToken = default
    )
    => Wrapper.RunAsync
    (
        fileName,
        arguments,
        Path.GetTempPath(),
        environmentOverrides,
        reportOutput,
        reportDiagnostic ?? (_ => { }),
        cancellationToken
    );

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

    private static string EchoToStderr(string text)
    => OperatingSystem.IsWindows() switch
    {
        true => $"[Console]::Error.WriteLine('{text.Replace("'", "''")}')",
        false => $"echo '{text.Replace("'", "'\\''")}' 1>&2",
    };

    [TestMethod]
    public async Task RunAsync_CapturesStdoutLines()
    {
        var lines = new List<string>();

        var result = await RunAsync(Shell, ["-c", Echo("hello")], reportOutput: lines.Add);

        Assert.IsInstanceOfType<ProcessSuccess>(result);
        Assert.IsTrue(lines.Any(l => l.Contains("hello")), $"Expected 'hello' in output. Got: [{string.Join(", ", lines)}]");
    }

    // Stderr is redirected, so a reader that drops it fills the pipe and wedges the child: the sink
    // is where those lines go, and nothing else in rix can see them.
    [TestMethod]
    public async Task RunAsync_ReportsStderrLines()
    {
        var diagnostics = new List<string>();

        var result = await RunAsync(Shell, ["-c", EchoToStderr("careful")], reportDiagnostic: diagnostics.Add);

        Assert.IsInstanceOfType<ProcessSuccess>(result);
        Assert.IsTrue(diagnostics.Any(l => l.Contains("careful")), $"Expected 'careful' in diagnostics. Got: [{string.Join(", ", diagnostics)}]");
    }

    [TestMethod]
    public async Task RunAsync_ReportsNonZeroExitCode()
    {
        var result = await RunAsync(Shell, ["-c", "exit 1"]);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        Assert.AreEqual("exited with code 1", ((ProcessFailure)result).Reason);
    }

    [TestMethod]
    public async Task RunAsync_Diagnostic_IsLastStderrLine_OnFailure()
    {
        var result = await RunAsync(Shell, ["-c", $"{EchoToStderr("boom")}; exit 1"]);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        Assert.AreEqual("boom", ((ProcessFailure)result).Diagnostic);
    }

    [TestMethod]
    public async Task RunAsync_Diagnostic_FallsBackToStdout_WhenStderrEmpty()
    {
        var result = await RunAsync(Shell, ["-c", $"{Echo("last stdout line")}; exit 1"]);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        StringAssert.Contains(((ProcessFailure)result).Diagnostic, "last stdout line");
    }

    [TestMethod]
    public async Task RunAsync_Diagnostic_IsNull_WhenNoOutputOnFailure()
    {
        var result = await RunAsync(Shell, ["-c", "exit 1"]);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        Assert.IsNull(((ProcessFailure)result).Diagnostic);
    }

    [TestMethod]
    public async Task RunAsync_PropagatesReportOutputException()
    {
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => RunAsync(
                Shell, ["-c", Echo("hello")],
                reportOutput: _ => throw new InvalidOperationException("callback failed")));
    }

    // Real stdout/stderr delivery from an OS pipe can't be forced to overlap on demand (stdout is
    // block-buffered by the shell and arrives in one end-of-process burst, while stderr trickles
    // line by line, so an end-to-end test can go many iterations without ever truly racing). Driving
    // ProcessWrapper.Synchronize directly with two threads under our own control instead proves the
    // guarantee deterministically.
    [TestMethod]
    public void Synchronize_BlocksASecondConcurrentCaller_UntilTheFirstReturns()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var synced = ProcessWrapper.Synchronize(new object(), line =>
        {
            if (line != "first") return;
            firstEntered.Set();
            Assert.IsTrue(releaseFirst.Wait(TimeSpan.FromSeconds(5)), "test setup: never released");
        })!;

        var firstCall = Task.Run(() => synced("first"));
        Assert.IsTrue(firstEntered.Wait(TimeSpan.FromSeconds(5)), "first call never entered the callback");

        var secondCall = Task.Run(() => synced("second"));
        // The second call is blocked on the same gate the first call is holding, so it must not be
        // able to complete yet - proving calls are serialized rather than merely usually-serialized.
        Assert.IsFalse(secondCall.Wait(TimeSpan.FromMilliseconds(200)), "second call ran concurrently with the first");

        releaseFirst.Set();
        Assert.IsTrue(Task.WaitAll([firstCall, secondCall], TimeSpan.FromSeconds(5)));
    }

    // What keeps a line of stderr out of the middle of a line of stdout is that the two sinks lock
    // the same gate - each being serial on its own would not.
    [TestMethod]
    public void Synchronize_BlocksASinkSharingAGate_UntilTheOtherReturns()
    {
        using var outputEntered = new ManualResetEventSlim();
        using var releaseOutput = new ManualResetEventSlim();
        var gate = new object();
        var output = ProcessWrapper.Synchronize(gate, _ =>
        {
            outputEntered.Set();
            Assert.IsTrue(releaseOutput.Wait(TimeSpan.FromSeconds(5)), "test setup: never released");
        })!;
        var diagnostic = ProcessWrapper.Synchronize(gate, _ => { })!;

        var outputCall = Task.Run(() => output("out"));
        Assert.IsTrue(outputEntered.Wait(TimeSpan.FromSeconds(5)), "the output sink never entered its callback");

        var diagnosticCall = Task.Run(() => diagnostic("err"));
        Assert.IsFalse(
            diagnosticCall.Wait(TimeSpan.FromMilliseconds(200)),
            "the diagnostic sink ran while the output sink held the gate");

        releaseOutput.Set();
        Assert.IsTrue(Task.WaitAll([outputCall, diagnosticCall], TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void Synchronize_ReturnsNull_WhenCallbackIsNull()
    => Assert.IsNull(ProcessWrapper.Synchronize(new object(), null));

    [TestMethod]
    public async Task RunAsync_TimesOut_WhenCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await RunAsync(Shell, ["-c", SleepSeconds(60)], cancellationToken: cts.Token);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        Assert.AreEqual("timed out", ((ProcessFailure)result).Reason);
    }

    [TestMethod]
    public async Task RunAsync_ReturnsFailure_WhenExecutableNotFound()
    {
        var result = await RunAsync("rix-no-such-executable", []);

        Assert.IsInstanceOfType<ProcessFailure>(result);
        StringAssert.Contains(((ProcessFailure)result).Reason, "rix-no-such-executable");
    }

    [TestMethod]
    public async Task RunAsync_InheritsParentEnv_WhenNoOverrides()
    {
        using var scope = new EnvScope();
        scope.Set("RIX_TEST_INHERIT", "inherited-value");

        var lines = new List<string>();
        await RunAsync(Shell, ["-c", PrintEnvOrFallback("RIX_TEST_INHERIT", "ABSENT")], reportOutput: lines.Add);

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
        await RunAsync(
            Shell,
            ["-c", PrintEnvOrFallback("RIX_TEST_INHERIT", "ABSENT")],
            environmentOverrides: overrides,
            reportOutput: inheritLines.Add);

        var overrideLines = new List<string>();
        await RunAsync(
            Shell,
            ["-c", PrintEnvOrFallback("RIX_TEST_OVERRIDE", "ABSENT")],
            environmentOverrides: overrides,
            reportOutput: overrideLines.Add);

        Assert.IsTrue(inheritLines.Any(l => l.Contains("inherited-value")), "Child should still inherit parent env when overrides provided");
        Assert.IsTrue(overrideLines.Any(l => l.Contains("overridden-value")), "Override should replace the original value");
    }

    // WorkingDirectory changes the child's real cwd without touching the PWD it inherits, and a
    // tool that reads PWD instead of calling getcwd() then acts on rix's own directory rather than
    // the one it was pointed at. opencode does exactly that, so the coding agent worked in whatever
    // directory rix was launched from instead of its clone - silently, in the wrong repository.
    //
    // Asserted against the start info rather than a real child, because a POSIX shell overwrites
    // PWD from getcwd() when it starts and so reports the right directory whether or not rix set it.
    [TestMethod]
    public void BuildStartInfo_SetsChildPwd_ToTheWorkingDirectory()
    {
        var startInfo = ProcessWrapper.BuildStartInfo(
            "some-tool", [], workingDirectory: Path.GetTempPath(), environmentOverrides: null);

        Assert.AreEqual(Path.GetFullPath(Path.GetTempPath()), startInfo.Environment["PWD"]);
    }

    // The path the child is told about has to be one it can act on from anywhere, so a relative
    // working directory - which .NET itself resolves against rix's cwd - must not reach it as-is.
    [TestMethod]
    public void BuildStartInfo_SetsChildPwd_ToAnAbsolutePath_WhenTheWorkingDirectoryIsRelative()
    {
        var startInfo = ProcessWrapper.BuildStartInfo(
            "some-tool", [], workingDirectory: ".", environmentOverrides: null);

        Assert.AreEqual(Path.GetFullPath("."), startInfo.Environment["PWD"]);
    }

    // The invariant is that the two never disagree, so PWD is applied after the overrides: an
    // override naming some other directory would put the child straight back into the state above.
    [TestMethod]
    public void BuildStartInfo_KeepsChildPwd_WhenAnOverrideNamesADifferentDirectory()
    {
        var workingDirectory = Directory.CreateTempSubdirectory("rix-pwd-");
        try
        {
            var startInfo = ProcessWrapper.BuildStartInfo(
                "some-tool",
                [],
                workingDirectory.FullName,
                new Dictionary<string, string> { ["PWD"] = Path.GetTempPath() });

            Assert.AreEqual(workingDirectory.FullName, startInfo.Environment["PWD"]);
        }
        finally
        {
            workingDirectory.Delete(recursive: true);
        }
    }
}
