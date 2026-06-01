using System.Diagnostics;

namespace Rix.Process;

internal record ProcessResult(int ExitCode, bool TimedOut)
{
    internal bool Succeeded => ExitCode == 0 && !TimedOut;
}

internal static class ProcessWrapper
{
    internal static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        CancellationToken cancellationToken = default,
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

        if (environmentOverrides is not null)
        {
            startInfo.Environment.Clear();
            foreach (var (key, value) in environmentOverrides)
                startInfo.Environment[key] = value;
        }

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
