using System.ComponentModel;
using System.Diagnostics;

namespace Rix.Process;

internal abstract record ProcessResult
{
    private protected ProcessResult() { }
}
/// <summary>A successful run. <paramref name="Output"/> is the final non-empty stdout line, or
/// <c>null</c> if the process wrote nothing — enough to read a terminal summary line without
/// buffering the whole stream.</summary>
internal sealed record ProcessSuccess(string? Output = null) : ProcessResult;
internal sealed record ProcessFailure(string Reason) : ProcessResult;

/// <summary>The single side-effect seam for running a subprocess. Every part of a job — agent
/// install, the agent run itself, and git operations — flows through one of these so effects stay
/// in one place and can be stubbed in tests.</summary>
internal delegate Task<ProcessResult> RunProcessAsync(
    string fileName,
    IEnumerable<string> arguments,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environmentOverrides,
    Action<string>? onStdoutLine,
    CancellationToken cancellationToken);

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
        try { process.Start(); }
        catch (Win32Exception ex) { return new ProcessFailure(ex.Message); }

        var stdoutTask = ReadLinesAsync(process.StandardOutput, onStdoutLine, cancellationToken);
        var processTask = process.WaitForExitAsync(cancellationToken);

        await Task.WhenAny(processTask, stdoutTask);
        if (stdoutTask.IsFaulted)
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* process already exited */ }

        try
        {
            await processTask;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* process already exited */ }
            await process.WaitForExitAsync(CancellationToken.None);
            await stdoutTask;
            return new ProcessFailure("timed out");
        }

        var lastLine = await stdoutTask;
        return process.ExitCode == 0 ? new ProcessSuccess(lastLine) : new ProcessFailure($"exited with code {process.ExitCode}");
    }

    /// <summary>Forwards each stdout line to <paramref name="onLine"/> and returns the final
    /// non-empty line read (or <c>null</c>).</summary>
    private static async Task<string?> ReadLinesAsync(
        StreamReader reader,
        Action<string>? onLine,
        CancellationToken cancellationToken)
    {
        string? lastLine = null;
        while (true)
        {
            string? line;
            try { line = await reader.ReadLineAsync(cancellationToken); }
            catch (OperationCanceledException) { return lastLine; }
            if (line is null) return lastLine;
            if (line.Length > 0) lastLine = line;
            onLine?.Invoke(line);
        }
    }
}
