using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Rix.Process;

internal sealed class ProcessWrapper
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
        Action<string> onStdoutLine,
        CancellationToken cancellationToken)
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
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
                onLine(line);
        }
        catch (OperationCanceledException) { }
    }

    private static async Task TerminateGracefullyAsync(System.Diagnostics.Process process)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                process.Kill();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                SendSigterm(process.Id);
            else
                process.Kill();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(cts.Token);
                return;
            }
            catch (OperationCanceledException) { }

            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void SendSigterm(int pid) =>
        kill(pid, 15);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);
}
