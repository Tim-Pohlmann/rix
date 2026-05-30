using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Rix.Process;

internal record ProcessResult(int ExitCode, bool TimedOut)
{
    internal bool Succeeded => ExitCode == 0 && !TimedOut;
}

internal static partial class ProcessWrapper
{
    private static readonly string[] AllowedEnvVars =
    [
        "PATH",
        "HOME",
        "ANTHROPIC_API_KEY",
        "CLAUDE_CODE_MAX_OUTPUT_TOKENS",
        "CLAUDE_CODE_DEBUG",
        "LANG",
        "LC_ALL",
    ];

    internal static IReadOnlyDictionary<string, string> BuildSanitizedEnvironment(
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var env = AllowedEnvVars
            .Select(key => (key, value: Environment.GetEnvironmentVariable(key)))
            .Where(kv => kv.value is not null)
            .ToDictionary(kv => kv.key, kv => kv.value!);

        if (overrides is not null)
            foreach (var (key, value) in overrides)
                env[key] = value;

        return env;
    }

    internal static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken,
        Action<string>? onStdoutLine = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
        };

        foreach (var arg in arguments)
            startInfo.ArgumentList.Add(arg);

        startInfo.Environment.Clear();
        foreach (var (key, value) in environment)
            startInfo.Environment[key] = value;

        using var process = new System.Diagnostics.Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();

        // Best-effort: promote child to its own process group so cancellation signals the full tree.
        // May fail with EACCES if the child exec'd before we got here; that's acceptable.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            TrySetProcessGroup(process.Id);

        var stdoutTask = ReadLinesAsync(process.StandardOutput, onStdoutLine, cancellationToken);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            await TerminateGracefullyAsync(process);
        }

        await stdoutTask;
        return new ProcessResult(timedOut ? -1 : process.ExitCode, timedOut);
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
                onLine?.Invoke(line);
        }
        catch (OperationCanceledException)
        {
            // Expected when the job is cancelled; stdout reading stops here.
        }
    }

    private static async Task TerminateGracefullyAsync(System.Diagnostics.Process process)
    {
        // Best-effort SIGTERM / soft kill; always fall through to hard kill after grace period.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try { SendSigterm(process.Id); }
            catch (InvalidOperationException)
            {
                // SIGTERM failed (process group gone or permission error); proceed to hard kill.
            }
        }
        else
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { return; }
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return;
        }
        catch (OperationCanceledException)
        {
            // Grace period elapsed; fall through to hard kill.
        }

        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException)
        {
            // Process already exited between the grace period and the hard kill.
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void TrySetProcessGroup(int pid)
    {
        if (setpgid(pid, pid) != 0 && Marshal.GetLastPInvokeError() is var errno && errno != 13 /* EACCES: child already exec'd */)
            throw new InvalidOperationException($"setpgid({pid}) failed: errno {errno}");
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void SendSigterm(int pid)
    {
        // Negative PID signals the entire process group.
        if (kill(-pid, 15) != 0)
            throw new InvalidOperationException($"kill(-{pid}, SIGTERM) failed: errno {Marshal.GetLastPInvokeError()}");
    }

    [LibraryImport("libc", SetLastError = true)]
    private static partial int kill(int pid, int sig);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int setpgid(int pid, int pgid);
}
