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
/// <summary><paramref name="Diagnostic"/> is <c>null</c> unless the process actually ran and
/// exited non-zero, in which case it's the last non-empty line it wrote (stderr preferred,
/// falling back to stdout) — the closest thing to "why" a CLI that doesn't structure its errors
/// gives us, e.g. an auth/usage message printed just before a non-zero exit. A process that never
/// started (see <see cref="Win32Exception"/>) or timed out carries no diagnostic.</summary>
internal sealed record ProcessFailure(string Reason, string? Diagnostic = null) : ProcessResult;

/// <summary>The single side-effect seam for running a subprocess. Every part of a job — agent
/// install, the agent run itself, and git operations — flows through one of these so effects stay
/// in one place and can be stubbed in tests.</summary>
internal delegate Task<ProcessResult> RunProcessAsync
(
    string fileName,
    IEnumerable<string> arguments,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environmentOverrides,
    Action<string>? onStdoutLine,
    CancellationToken cancellationToken
);

internal static class ProcessWrapper
{
    internal static async Task<ProcessResult> RunAsync
    (
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        Action<string>? onStdoutLine = null,
        CancellationToken cancellationToken = default
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
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

        // Stdout and stderr are drained by two concurrent tasks (see below), so a caller-supplied
        // callback can now be invoked from either one at the same time. Synchronizing it here - once,
        // shared by both call sites - keeps calls serialized regardless of which stream a line came
        // from, instead of leaving every future caller to notice and guard against the race itself.
        var syncedOnStdoutLine = Synchronize(onStdoutLine);
        var stdoutTask = ReadLinesAsync(process.StandardOutput, syncedOnStdoutLine, cancellationToken);
        // Both streams must be drained concurrently (not just stdout), or a child that fills its
        // stderr pipe while nothing is reading it can deadlock the whole run.
        var stderrTask = ReadLinesAsync(process.StandardError, BuildStderrForwarder(syncedOnStdoutLine), cancellationToken);
        var processTask = process.WaitForExitAsync(cancellationToken);

        await Task.WhenAny(processTask, stdoutTask, stderrTask);
        if (stdoutTask.IsFaulted || stderrTask.IsFaulted)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* process already exited */ }
            // Drain both now, before whichever fault propagates below - otherwise this method
            // can return (or throw) without ever awaiting one of them, leaving its read loop
            // running against a stream whose process is about to be disposed.
            try { await stdoutTask; }
            catch { /* best-effort: whichever task actually faulted first is what fails this run */ }
            try { await stderrTask; }
            catch { /* same */ }
        }

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
            await stderrTask;
            return new ProcessFailure("timed out");
        }

        var lastLine = await stdoutTask;
        var lastErrLine = await stderrTask;
        if (process.ExitCode == 0)
            return new ProcessSuccess(lastLine);
        return new ProcessFailure($"exited with code {process.ExitCode}", Diagnostic: lastErrLine ?? lastLine);
    }

    /// <summary>Wraps <paramref name="callback"/> so calls through the returned delegate never
    /// overlap, even when made concurrently from stdout's and stderr's independent reader tasks -
    /// callers (test spies, non-thread-safe loggers) are entitled to assume one line is fully
    /// handled before the next arrives. Internal (rather than private) so this guarantee can be
    /// pinned down directly, deterministically, from a test - real stdout/stderr delivery from an
    /// actual OS pipe can't be forced to overlap on demand across platforms.</summary>
    internal static Action<string>? Synchronize(Action<string>? callback)
    {
        if (callback is null) return null;
        var gate = new object();
        return line => { lock (gate) { callback(line); } };
    }

    /// <summary>When a callback is supplied, stderr is forwarded through it prefixed, since stdout
    /// is forwarded through that same callback and the prefix is what keeps the two
    /// distinguishable there. Callers that pass <c>onStdoutLine: null</c> (git, npm) relied on
    /// stderr being inherited straight from the console before this method started
    /// redirecting/capturing it, so those get the raw line written straight to
    /// <see cref="Console.Error"/> instead - its own stream, so no prefix is needed to
    /// disambiguate it.</summary>
    private static Action<string> BuildStderrForwarder(Action<string>? onStdoutLine)
    {
        if (onStdoutLine is null) return Console.Error.WriteLine;
        return line => onStdoutLine($"[stderr] {line}");
    }

    /// <summary>Forwards each line read from <paramref name="reader"/> to <paramref name="onLine"/>
    /// and returns the final non-empty line read (or <c>null</c>).</summary>
    private static async Task<string?> ReadLinesAsync
    (
        StreamReader reader,
        Action<string>? onLine,
        CancellationToken cancellationToken
    )
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
