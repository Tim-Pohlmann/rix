using System.Diagnostics;

namespace Rix.Process;

internal record ProcessResult(int ExitCode, bool TimedOut)
{
    internal bool Succeeded => ExitCode == 0 && !TimedOut;
}

internal static class ProcessWrapper
{
    private static readonly string[] AllowedEnvVars =
    [
        "PATH",
        "HOME",
        "ANTHROPIC_API_KEY",
        "CLAUDE_CODE_MAX_OUTPUT_TOKENS",
        "LANG",
        "LC_ALL",
    ];

    internal static Dictionary<string, string> BuildSanitizedEnvironment() =>
        AllowedEnvVars
            .Select(key => (key, value: Environment.GetEnvironmentVariable(key)))
            .Where(kv => kv.value is not null)
            .ToDictionary(kv => kv.key, kv => kv.value!);

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

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = ReadLinesAsync(process.StandardOutput, onStdoutLine, cancellationToken);
        var processTask = process.WaitForExitAsync(cancellationToken);

        await Task.WhenAny(processTask, stdoutTask);
        if (stdoutTask.IsFaulted)
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* process already exited */ }

        bool timedOut;
        try
        {
            await processTask;
            timedOut = false;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* process already exited */ }
            await process.WaitForExitAsync(CancellationToken.None);
        }

        await stdoutTask;
        return new ProcessResult(timedOut ? -1 : process.ExitCode, timedOut); // NOSONAR
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line;
            try { line = await reader.ReadLineAsync(cancellationToken); }
            catch (OperationCanceledException) { return; }
            if (line is null) return;
            onLine?.Invoke(line);
        }
    }
}
