using Rix.Agents;
using Rix.CiFailure;
using Rix.Cli;
using Rix.Initialize;
using Rix.Job;
using Rix.Process;
using Rix.Repository;
using Rix.Submit;
using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Rix;

internal static class Startup
{
    /// <summary>The production <see cref="JobContext"/>: git against the GitHub repo, process runner,
    /// the coding agent selected by <see cref="JobConfig.Agent"/>, and stderr log sink, all wired
    /// from <paramref name="config"/>. <see cref="JobContext.TranscriptLine"/> is a no-op here;
    /// <see cref="ExecuteJobAsync"/> tees in its own collecting sink regardless of which context
    /// it ends up using.</summary>
    internal static JobContext DefaultContext(JobConfig config)
    => new
    (
        GitHubGit(config.Repo, config.ReadToken),
        ProcessWrapper.RunAsync,
        SelectAgent(config.Agent.Kind),
        // Named because LogLine and TranscriptLine are the same delegate type: transposing them
        // compiles, and would silently print the agent's transcript to stderr and drop rix's own log.
        LogLine: Console.Error.WriteLine,
        TranscriptLine: _ => { },
        AgentHomeFetcher: new AgentHomeFetcher(factoryRepo => GitHubGit(factoryRepo, config.ReadToken))
    );

    /// <summary>Git against <paramref name="repo"/> on GitHub, authenticated with
    /// <paramref name="token"/> — the one place the GitHub clone URL is spelled out.</summary>
    private static GitCli GitHubGit(RepoIdentifier repo, GitReadToken token)
    => new(new Uri($"https://github.com/{repo.Value}.git"), token, ProcessWrapper.RunAsync);

    private static ICodingAgent SelectAgent(AgentKind agent)
    => agent switch
    {
        AgentKind.Claude => new ClaudeAgent(),
        AgentKind.OpenCode => new OpenCodeAgent(),
        AgentKind.Pi => new PiAgent(),
        _ => throw new NotSupportedException($"Unsupported agent: {agent}"),
    };

    /// <summary>The production <see cref="CiFailureContext"/>: reading the run and judging its
    /// branch are two roles against the same GitHub account under the same credential, so they are
    /// two hosts over one shared <see cref="GitHubApi"/> transport rather than two independently
    /// connected ones. This is where GitHub-hosting-its-own-CI is asserted — the seams themselves
    /// don't require it, and pointing <see cref="CiFailureContext.Ci"/> at another CI provider is a
    /// change to this method alone. Built only when no context was supplied, so a test that brings
    /// its own stubs opens no connection at all.</summary>
    internal static CiFailureContext DefaultCiFailureContext(CiFailureConfig config)
    {
        var api = new GitHubApi(config.Repo, config.ReadToken);
        return new CiFailureContext(new GitHubActionsCiHost(api), new GitHubCiFailureRepoHost(api));
    }

    /// <summary>The production <see cref="SubmitContext"/>: git and the GitHub repo host, both
    /// authenticated with the write token, the default process runner, and a stderr log sink.</summary>
    internal static SubmitContext DefaultSubmitContext(SubmitConfig config)
    => new
    (
        GitHubGit(config.Repo, config.WriteToken),
        new GitHubSubmitRepoHost(config.Repo, config.WriteToken),
        ProcessWrapper.RunAsync,
        Console.Error.WriteLine
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

            var rootCommand = new RootCommand("RIX - AI-powered code automation")
            {
                Runs(JobCommand.Build(), JobCommand.ReadConfig, config => ExecuteJobAsync(config, cts.Token)),
                Runs(SubmitCommand.Build(), SubmitCommand.ReadConfig, config => ExecuteSubmitAsync(config, cts.Token)),
                Runs(CiFailureCommand.Build(), CiFailureCommand.ReadConfig, config => ExecuteCiFailureAsync(config, cts.Token)),
                Runs(InitializeCommand.Build(), InitializeCommand.ReadConfig, config => ExecuteInitializeAsync(config, cts.Token)),
            };
            return await CliPipeline.InvokeAsync(rootCommand, args);
        }
        finally
        {
            Console.CancelKeyPress -= onCancelKeyPress;
        }
    }

    /// <summary>Makes invoking <paramref name="command"/> read its config and run it. The read happens
    /// inside the action so an <see cref="InvalidInputException"/> it throws is reported like any
    /// other (see <see cref="CliPipeline.ReportingInvalidInput"/>).</summary>
    private static Command Runs<TConfig>(Command command, Func<ParseResult, TConfig> read, Func<TConfig, Task<int>> execute)
    {
        command.SetAction(CliPipeline.ReportingInvalidInput(parsed => execute(read(parsed))));
        return command;
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
        var result = await JobRunner.RunAsync(config, Teeing(context ?? DefaultContext(config), transcriptLines), cancellationToken);
        return await WriteJobResultAsync(config, result, transcriptLines);
    }

    /// <summary>Wraps <paramref name="context"/>'s transcript sink so every line it emits is also
    /// collected into <paramref name="transcriptLines"/>, which <see cref="WriteJobResultAsync"/>
    /// later writes to <c>transcript.md</c>. Tees rather than replaces, so whatever the context
    /// already did with each line (printing it, in the default case) still happens.</summary>
    private static JobContext Teeing(JobContext context, List<string> transcriptLines)
    {
        var transcriptSink = context.TranscriptLine;
        return context with { TranscriptLine = line => { transcriptSink(line); transcriptLines.Add(line); } };
    }

    /// <summary>
    /// Writes a job's outcome the same way regardless of what led to it: the result JSON to
    /// stdout, <c>result.json</c> to <paramref name="config"/>'s output dir (even on failure, so
    /// downstream tooling has one reliable place to read the outcome from), and
    /// <c>transcript.md</c> if the agent said anything worth keeping.
    /// </summary>
    private static async Task<int> WriteJobResultAsync(JobConfig config, IJobResult result, List<string> transcriptLines)
    {
        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        // Best-effort: once the job outcome above is decided, a broken/closed stdout pipe must not
        // stop the correct exit code from being returned any more than a result.json write failure
        // does below.
        await WriteBestEffortAsync(Console.Out, json);
        await WriteOutputFileBestEffortAsync(config.OutputDir, "result.json", json);
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
        await WriteOutputFileBestEffortAsync(config.OutputDir, "transcript.md", string.Join("\n\n", transcriptLines));
    }

    /// <summary>Writes <paramref name="content"/> to <paramref name="name"/> in
    /// <paramref name="outputDir"/>, reporting a failure to stderr instead of raising it.
    /// Best-effort and uncancellable: every caller runs after the outcome it is recording has
    /// already been decided, so a cancellation requested in that narrow window - or a transient
    /// disk error - must not stop the correct exit code from being returned. Only the file is
    /// lost, and the same JSON has already gone to stdout.</summary>
    private static async Task WriteOutputFileBestEffortAsync(DirectoryPath outputDir, string name, string content)
    {
        try
        {
            await File.WriteAllTextAsync(Path.Combine(outputDir.Value, name), content, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Also best-effort: a closed/broken stderr must not defeat the exit-code guarantee.
            await WriteBestEffortAsync(Console.Error, $"warning: failed to write {name}: {ex.Message}");
        }
    }

    /// <summary>Writes <paramref name="line"/> to <paramref name="writer"/>, swallowing the ways a
    /// closed/broken console stream can fail a write (<see cref="IOException"/> for a broken pipe,
    /// <see cref="ObjectDisposedException"/> if the stream was already disposed) - used for output
    /// that must never prevent the correct exit code from being returned.</summary>
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
    /// Imperative shell around <see cref="CiFailureRunner.RunAsync"/>: reports what the check
    /// found and stops. No agent runs here, and nothing is cloned — whoever answers the failure
    /// does so from a <see cref="CiFailureDetected.Prompt"/> this wrote, on a machine this one
    /// never touches.
    ///
    /// The verdict goes to stdout (for a caller reading the JSON directly) and to
    /// <c>result.json</c> (for one that would rather read a file); when a failure was detected the
    /// prompt goes to <c>prompt.md</c>, and the verdict carries its path rather than its text. The
    /// prompt is the one field built from the failing run's log output, so it is the one with no
    /// bound on its length or its content — naming the file instead of inlining it means the
    /// verdict stays short and validated, and nobody parsing it has to quote arbitrary log text
    /// back out of a field. That is also why the file is written first: the verdict only points at
    /// it once it is there, and a prompt that could not be written is reported as an error rather
    /// than announced as a detection.
    /// </summary>
    internal static async Task<int> ExecuteCiFailureAsync(CiFailureConfig config, CancellationToken cancellationToken, CiFailureContext? context = null)
    {
        var collaborators = context ?? DefaultCiFailureContext(config);
        var result = await CiFailureRunner.RunAsync
        (
            config.Repo, config.RunId, collaborators.Ci, collaborators.RepoHost, config.MaxRixCommits, cancellationToken
        );

        if (result is CiFailureDetected detected)
            result = await WritePromptAsync(detected, config.OutputDir);

        var json = JsonSerializer.Serialize(result, CiFailureJsonContext.Default.ICiFailureResult);
        await WriteBestEffortAsync(Console.Out, json);
        await WriteOutputFileBestEffortAsync(config.OutputDir, "result.json", json);

        // A detected failure exits successfully like the rest: it is a verdict, not an outcome, and
        // the caller decides what to do with it. Only CiFailureError - a problem talking to the API,
        // rather than the run itself having failed - is a failure of this command.
        return result switch
        {
            CiFailureDetected or CiFailureSkipped or CiFailureLoopGuarded or CiFailureUntrustedRun => ExitCodes.Success,
            CiFailureError => ExitCodes.JobFailed,
            _ => throw new NotSupportedException($"Unexpected ci-failure result type: {result.GetType()}"),
        };
    }

    /// <summary>Writes the prompt beside the verdict and returns the verdict naming it. Unlike the
    /// best-effort writes elsewhere here, a failure is turned into a <see cref="CiFailureError"/>:
    /// this one runs before the verdict is reported rather than after, and a <c>detected</c> result
    /// whose prompt never reached disk would send a caller to a file that isn't there. Reporting it
    /// as an error instead leaves the caller with no branch and no agent started, which is what a
    /// check that couldn't finish should produce.</summary>
    private static async Task<ICiFailureResult> WritePromptAsync(CiFailureDetected detected, DirectoryPath outputDir)
    {
        var path = Path.Combine(outputDir.Value, "prompt.md");
        try
        {
            await File.WriteAllTextAsync(path, detected.Prompt, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CiFailureError($"failed to write the prompt to {path}: {ex.Message}");
        }

        return detected with { PromptFile = path };
    }

    /// <summary>The production <see cref="InitializeContext"/>: writes each template to disk via
    /// <see cref="FileWriter"/> (creating any missing parent directory), and logs to stderr.</summary>
    private static InitializeContext DefaultInitializeContext()
    => new
    (
        FileWriter.WriteAsync,
        Console.Error.WriteLine
    );

    /// <summary>
    /// Imperative shell around <see cref="InitializeRunner.RunAsync"/>: writes the caller
    /// workflows, then prints the manual follow-up steps (secrets, the CI workflow name, commit)
    /// on success, or the error on failure, and maps the result to an exit code.
    /// </summary>
    internal static async Task<int> ExecuteInitializeAsync(InitializeConfig config, CancellationToken cancellationToken, InitializeContext? context = null)
    {
        context ??= DefaultInitializeContext();
        var result = await InitializeRunner.RunAsync(config, context, cancellationToken);
        switch (result)
        {
            case InitializeSuccess:
                await Console.Error.WriteLineAsync(NextStepsGuidance);
                return ExitCodes.Success;
            case InitializeFailure failure:
                await Console.Error.WriteLineAsync($"error: {failure.Error}");
                return ExitCodes.SetupFailed;
            default:
                throw new NotSupportedException($"Unexpected initialize result type: {result.GetType()}");
        }
    }

    /// <summary>The steps <c>rix initialize</c> can't do itself: it only writes files, so setting
    /// secrets, naming the watched CI workflow, and committing are left to the user.</summary>
    private const string NextStepsGuidance = """

        Next steps:
          1. Add repo secrets (Settings -> Secrets and variables -> Actions):
               RIX_READ_TOKEN   PAT, contents:read  (+ actions:read for the CI-failure workflow)
               RIX_WRITE_TOKEN  PAT, contents:write + pull-requests:write
          2. In rix-on-ci-failure.yml, set workflows: ["CI"] to your CI workflow's name.
          3. Commit and push the two workflow files.
        """;
}
