using Rix.Agents;
using Rix.CiFailure;
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
    /// from <paramref name="config"/>. <see cref="JobContext.TranscriptLine"/> is a no-op here;
    /// <see cref="ExecuteJobAsync"/> tees in its own collecting sink regardless of which context
    /// it ends up using.</summary>
    internal static JobContext DefaultContext(JobConfig config)
    => DefaultContext(config, new GitHubReadHost(config.Repo, config.ReadToken, ProcessWrapper.RunAsync));

    /// <summary>Overload for callers (e.g. <see cref="ExecuteCiFailureJobAsync"/>) that already
    /// have a host instance to reuse — e.g. one also serving as the <see cref="ICiFailureHost"/>
    /// for the same run, rather than opening a second, redundant connection.</summary>
    internal static JobContext DefaultContext(JobConfig config, IRepositoryReadHost host)
    => new
    (
        Host: host,
        RunProcess: ProcessWrapper.RunAsync,
        Agent: SelectAgent(config.Agent.Kind),
        LogLine: Console.Error.WriteLine,
        TranscriptLine: _ => { }
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
            rootCommand.AddCommand(CiFailureCommand.Build(config => ExecuteCiFailureAsync(config, cts.Token)));
            rootCommand.AddCommand(CiFailureJobCommand.Build(config => ExecuteCiFailureJobAsync(config, cts.Token)));
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
        context ??= DefaultContext(config);
        var transcriptSink = context.TranscriptLine;
        context = context with { TranscriptLine = line => { transcriptSink(line); transcriptLines.Add(line); } };
        var result = await JobRunner.RunAsync(config, context, cancellationToken);
        return await WriteJobResultAsync(config, result, transcriptLines);
    }

    /// <summary>
    /// Writes a job's outcome the same way regardless of what led to it: the result JSON to
    /// stdout, <c>result.json</c> to <paramref name="config"/>'s output dir (even on failure, so
    /// downstream tooling has one reliable place to read the outcome from), and
    /// <c>transcript.md</c> if the agent said anything worth keeping. Shared by
    /// <see cref="ExecuteJobAsync"/> and <see cref="ExecuteCiFailureJobAsync"/>, which only differ
    /// in how they arrive at <paramref name="result"/>.
    /// </summary>
    private static async Task<int> WriteJobResultAsync(JobConfig config, IJobResult result, List<string> transcriptLines)
    {
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

    /// <summary>
    /// Imperative shell around <see cref="CiFailureRunner.RunAsync"/>: runs the check, writes the
    /// result JSON to stdout, and maps the result to an exit code. <see cref="CiFailureSkipped"/>
    /// exits successfully (there was simply nothing to do); only <see cref="CiFailureError"/> — a
    /// problem talking to the API, not the run itself failing — is treated as a job failure.
    /// </summary>
    internal static async Task<int> ExecuteCiFailureAsync(CiFailureConfig config, CancellationToken cancellationToken, ICiFailureHost? host = null)
    {
        host ??= new GitHubReadHost(config.Repo, config.ReadToken, ProcessWrapper.RunAsync);
        var result = await CiFailureRunner.RunAsync(config, host, cancellationToken);
        return WriteCiFailureResult(result);
    }

    /// <summary>Writes a ci-failure check's outcome the same way regardless of whether the agent
    /// then ran: the result JSON to stdout, mapped to an exit code. Shared by
    /// <see cref="ExecuteCiFailureAsync"/> and <see cref="ExecuteCiFailureJobAsync"/>, the latter
    /// only ever passing a <see cref="CiFailureSkipped"/> or <see cref="CiFailureError"/> here —
    /// <see cref="CiFailureDetected"/> always leads to <see cref="WriteJobResultAsync"/> instead.</summary>
    private static int WriteCiFailureResult(ICiFailureResult result)
    {
        var json = JsonSerializer.Serialize(result, CiFailureJsonContext.Default.ICiFailureResult);
        Console.WriteLine(json);
        return result switch
        {
            CiFailureDetected or CiFailureSkipped => ExitCodes.Success,
            CiFailureError => ExitCodes.JobFailed,
            _ => throw new NotSupportedException($"Unexpected ci-failure result type: {result.GetType()}"),
        };
    }

    /// <summary>
    /// Imperative shell around <see cref="CiFailureJobRunner.RunAsync"/>: checks whether the run
    /// failed and, only if it did, runs the agent — reusing <see cref="WriteCiFailureResult"/> and
    /// <see cref="WriteJobResultAsync"/> so each outcome is reported identically to its standalone
    /// <c>rix ci-failure</c>/<c>rix job</c> counterpart. One <see cref="GitHubReadHost"/> backs
    /// both the ci-failure check and the job's clone, since it implements both roles.
    /// </summary>
    internal static async Task<int> ExecuteCiFailureJobAsync(CiFailureJobConfig config, CancellationToken cancellationToken, ICiFailureHost? ciFailureHost = null, JobContext? jobContext = null)
    {
        if (ciFailureHost is null || jobContext is null)
        {
            var host = new GitHubReadHost(config.Job.Repo, config.Job.ReadToken, ProcessWrapper.RunAsync);
            ciFailureHost ??= host;
            jobContext ??= DefaultContext(config.Job, host);
        }

        var transcriptLines = new List<string>();
        var transcriptSink = jobContext.TranscriptLine;
        jobContext = jobContext with { TranscriptLine = line => { transcriptSink(line); transcriptLines.Add(line); } };

        var outcome = await CiFailureJobRunner.RunAsync(config, ciFailureHost, jobContext, cancellationToken);
        return outcome switch
        {
            CiFailureJobNotRun(var reason) => WriteCiFailureResult(reason),
            CiFailureJobRan(var result) => await WriteJobResultAsync(config.Job, result, transcriptLines),
            _ => throw new NotSupportedException($"Unexpected ci-failure-job outcome: {outcome.GetType()}"),
        };
    }
}
