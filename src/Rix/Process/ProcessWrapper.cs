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

/// <summary>The production <see cref="IProcessRunner"/>: a real child process, with both its
/// streams drained concurrently so neither can fill its pipe and deadlock the run.</summary>
internal sealed class ProcessWrapper : IProcessRunner
{
    public async Task<ProcessResult> RunAsync
    (
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        Action<string>? reportOutput,
        Action<string> reportDiagnostic,
        CancellationToken cancellationToken
    )
    {
        var startInfo = BuildStartInfo(fileName, arguments, workingDirectory, environmentOverrides);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        try { process.Start(); }
        catch (Win32Exception ex) { return new ProcessFailure(ex.Message); }

        // Stdout and stderr are drained by two concurrent tasks (see below), so either sink can be
        // invoked from either one at the same time. One gate shared by both - rather than one each -
        // keeps every call serialized regardless of which stream a line came from, which is what a
        // caller sending both to the same log needs and what no caller can arrange for itself.
        var gate = new object();
        var stdoutTask = ReadLinesAsync(process.StandardOutput, Synchronize(gate, reportOutput), cancellationToken);
        // Both streams must be drained concurrently (not just stdout), or a child that fills its
        // stderr pipe while nothing is reading it can deadlock the whole run.
        var stderrTask = ReadLinesAsync(process.StandardError, Synchronize(gate, reportDiagnostic), cancellationToken);
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

    /// <summary>Builds the child's start info: its executable, argument list, working directory and
    /// environment. Internal (rather than inlined into <see cref="RunAsync"/>) so the PWD guarantee
    /// below can be pinned down directly and deterministically, on every platform - a real child
    /// can't be used to observe it, because a POSIX shell overwrites PWD from getcwd() on startup
    /// and so reports the right answer either way.</summary>
    internal static ProcessStartInfo BuildStartInfo
    (
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentOverrides
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

        // WorkingDirectory sets the child's real cwd but leaves the PWD it inherits pointing at
        // rix's own, and a tool that trusts PWD over getcwd() then works in an entirely different
        // directory. opencode does exactly that: the coding agent edited and committed in whatever
        // directory rix was launched from rather than the clone it was handed - on CI, the runner's
        // own checkout - which only ever surfaced because POST /pr rejects a branch that is missing
        // from the clone. A POSIX shell keeps the two in step on every cd; so does this, once here
        // instead of per-caller. Applied after the overrides because the invariant is that PWD and
        // the working directory never disagree, not something a caller should be able to desync.
        startInfo.Environment["PWD"] = Path.GetFullPath(workingDirectory);
        return startInfo;
    }

    /// <summary>Wraps <paramref name="sink"/> so calls through the returned delegate never overlap
    /// with any other call holding <paramref name="gate"/> - including the other stream's sink, when
    /// both are given the same gate, which is how stdout's and stderr's independent reader tasks are
    /// kept from interleaving two lines into one log. Callers (test spies, non-thread-safe loggers)
    /// are entitled to assume one line is fully handled before the next arrives. Internal (rather
    /// than private) so this guarantee can be pinned down directly, deterministically, from a test -
    /// real stdout/stderr delivery from an actual OS pipe can't be forced to overlap on demand
    /// across platforms.</summary>
    internal static Action<string>? Synchronize(object gate, Action<string>? sink)
    {
        if (sink is null) return null;
        return line => { lock (gate) { sink(line); } };
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
