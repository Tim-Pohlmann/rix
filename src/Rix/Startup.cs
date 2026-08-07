using Rix.Agents;
using Rix.Cli;
using Rix.Job;
using Rix.Process;
using Rix.Repository;
using Rix.Submit;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Rix;

internal static class Startup
{
    /// <summary>The production <see cref="JobContext"/>: real GitHub host, process runner,
    /// the coding agent selected by <see cref="JobConfig.Agent"/>, and stderr log sink, all wired
    /// from <paramref name="config"/>. <paramref name="transcriptLine"/> is the sink each extracted
    /// agent transcript chunk is forwarded to; the shell (see <see cref="ExecuteJobAsync"/>) is
    /// the caller that supplies it.</summary>
    internal static JobContext DefaultContext(JobConfig config, LogLine? transcriptLine = null)
    => new
    (
        Host: new GitHubReadHost(config.Repo, config.ReadToken, ProcessWrapper.RunAsync),
        RunProcess: ProcessWrapper.RunAsync,
        Agent: SelectAgent(config.Agent.Kind),
        LogLine: Console.Error.WriteLine,
        TranscriptLine: transcriptLine ?? (_ => { })
    );

    private static ICodingAgent SelectAgent(AgentKind agent)
    => agent switch
    {
        AgentKind.Claude => new ClaudeAgent(),
        AgentKind.OpenCode => new OpenCodeAgent(),
        AgentKind.Pi => new PiAgent(),
        _ => throw new NotSupportedException($"Unsupported agent: {agent}"),
    };

    /// <summary>The production <see cref="SubmitContext"/>: a GitHub host authenticated with the
    /// write token, the default process runner, and a stderr log sink.</summary>
    internal static SubmitContext DefaultSubmitContext(SubmitConfig config)
    => new
    (
        Host: new GitHubHost(config.Repo, config.WriteToken, ProcessWrapper.RunAsync),
        RunProcess: ProcessWrapper.RunAsync,
        LogLine: Console.Error.WriteLine
    );

    /// <summary>
    /// Cancels in-flight work on Ctrl+C (<see cref="Console.CancelKeyPress"/>) or SIGTERM
    /// (<see cref="PosixSignalRegistration"/>, e.g. <c>docker stop</c> or a Kubernetes pod
    /// eviction), so a mid-flight git/agent/HTTP call unwinds instead of being killed outright.
    /// Both handlers are unregistered once the run completes, so repeated calls (as in tests)
    /// don't accumulate stale handlers.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args)
    {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancelKeyPress = (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += onCancelKeyPress;
        try
        {
            using var onSigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandleSigterm(cts));

            var rootCommand = new RootCommand("RIX - AI-powered code automation");
            rootCommand.AddCommand(JobCommand.Build(config => ExecuteJobAsync(config, cts.Token)));
            rootCommand.AddCommand(SubmitCommand.Build(config => ExecuteSubmitAsync(config, cts.Token)));
            return await new CommandLineBuilder(rootCommand).UseDefaults().Build().InvokeAsync(args);
        }
        finally
        {
            Console.CancelKeyPress -= onCancelKeyPress;
        }
    }

    /// <summary>Cancels <paramref name="cts"/> and suppresses the runtime's default termination so
    /// the in-flight run unwinds gracefully. Extracted from <see cref="RunAsync"/> so the SIGTERM
    /// path is unit-testable: unlike <see cref="ConsoleCancelEventArgs"/>, <see cref="PosixSignalContext"/>
    /// has a public constructor.</summary>
    internal static Action<PosixSignalContext> HandleSigterm(CancellationTokenSource cts)
    => ctx =>
    {
        ctx.Cancel = true;
        cts.Cancel();
    };

    /// <summary>
    /// Imperative shell around the pure-ish <see cref="JobRunner.RunAsync"/> core: runs the job,
    /// then performs all output effects — forwards the agent's stream to stderr, writes the result
    /// JSON to stdout, persists <c>result.json</c> regardless of outcome, writes the agent's
    /// extracted transcript to <c>transcript.md</c>, and maps the result to an exit code. Writing
    /// <c>result.json</c> on failure too (not just success) means callers — including
    /// <c>rix submit</c>, which already rejects a non-success <c>result.json</c> — have one
    /// reliable place to read the outcome from, instead of scraping stdout. <c>transcript.md</c>
    /// is best-effort the same way: it's a human-readable log of what the agent said/did, skipped
    /// entirely when nothing was extracted.
    /// </summary>
    internal static async Task<int> ExecuteJobAsync(JobConfig config, CancellationToken cancellationToken, JobContext? context = null)
    {
        var transcriptLines = new List<string>();
        if (context is null)
        {
            context = DefaultContext(config, transcriptLines.Add);
        }
        else
        {
            var transcriptLine = context.TranscriptLine;
            context = context with { TranscriptLine = line => { transcriptLine(line); transcriptLines.Add(line); } };
        }
        var result = await JobRunner.RunAsync(config, context, cancellationToken);
        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        // Best-effort: once the job outcome above is decided, a broken/closed stdout pipe must not
        // stop the correct exit code from being returned any more than a result.json write failure
        // does below.
        await WriteBestEffortAsync(Console.Out, json);
        // Best-effort and uncancellable: this runs after the job itself is already decided, so a
        // cancellation requested in this narrow window (or a transient disk error) must not stop
        // the correct exit code from being returned - only the result.json copy would be lost.
        try
        {
            await File.WriteAllTextAsync(Path.Combine(config.OutputDir.Value, "result.json"), json, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Also best-effort: a closed/broken stderr must not defeat the exit-code guarantee above.
            await WriteBestEffortAsync(Console.Error, $"warning: failed to write result.json: {ex.Message}");
        }
        await WriteTranscriptAsync(config, transcriptLines);
        return result switch
        {
            JobSuccess => ExitCodes.Success,
            JobFailure => ExitCodes.JobFailed,
            SetupFailure => ExitCodes.SetupFailed,
            _ => throw new NotSupportedException($"Unexpected job result type: {result.GetType()}"),
        };
    }

    /// <summary>
    /// Persists the collected agent transcript to <c>transcript.md</c> in the output directory,
    /// joining each extracted chunk with a blank line. Best-effort and uncancellable, mirroring the
    /// <c>result.json</c> write above: a disk error must never affect the exit code. Skipped
    /// entirely when nothing was extracted, so the artifact only exists when there is content.
    /// </summary>
    private static async Task WriteTranscriptAsync(JobConfig config, List<string> transcriptLines)
    {
        if (transcriptLines.Count == 0) return;
        try
        {
            await File.WriteAllTextAsync
            (
                Path.Combine(config.OutputDir.Value, "transcript.md"),
                string.Join("\n\n", transcriptLines),
                CancellationToken.None
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await WriteBestEffortAsync(Console.Error, $"warning: failed to write transcript.md: {ex.Message}");
        }
    }

    /// <summary>Writes <paramref name="line"/> to <paramref name="writer"/>, swallowing the ways a
    /// closed/broken console stream can fail a write (<see cref="IOException"/> for a broken pipe,
    /// <see cref="ObjectDisposedException"/> if the stream was already disposed) - used by
    /// <see cref="ExecuteJobAsync"/> for output that must never prevent the correct exit code from
    /// being returned.</summary>
    private static async Task WriteBestEffortAsync(TextWriter writer, string line)
    {
        try { await writer.WriteLineAsync(line); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* nothing left to report to */ }
    }

    /// <summary>
    /// Imperative shell around <see cref="SubmitRunner.RunAsync"/>: runs the submit, writes the
    /// result JSON to stdout, and maps the result to an exit code.
    /// </summary>
    internal static async Task<int> ExecuteSubmitAsync(SubmitConfig config, CancellationToken cancellationToken, SubmitContext? context = null)
    {
        context ??= DefaultSubmitContext(config);
        var result = await SubmitRunner.RunAsync(config, context, cancellationToken);
        var json = JsonSerializer.Serialize(result, SubmitJsonContext.Default.ISubmitResult);
        Console.WriteLine(json);
        return result switch
        {
            SubmitSuccess => ExitCodes.Success,
            SubmitFailure => ExitCodes.JobFailed,
            _ => throw new NotSupportedException($"Unexpected submit result type: {result.GetType()}"),
        };
    }
}
