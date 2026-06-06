using System.Diagnostics;

namespace Rix.Process;

internal abstract record ProcessResult;
internal sealed record ProcessSuccess(string? Output = null) : ProcessResult;
internal sealed record ProcessFailure(string Reason) : ProcessResult;

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
        if (timedOut) return new ProcessFailure("timed out");
        return process.ExitCode == 0 ? new ProcessSuccess() : new ProcessFailure($"exited with code {process.ExitCode}");
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
