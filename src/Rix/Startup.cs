using Rix.Cli;
using System.CommandLine;
using System.Runtime.InteropServices;

namespace Rix;

/// <summary>The process: the signals that cancel a run, the set of commands rix offers, and the
/// invocation. What each command then does is the command's own (see <see cref="Rix.Cli"/>), and
/// what it runs against is <see cref="Composition"/>'s.</summary>
internal static class Startup
{
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

            var fileSystem = Composition.FileSystem();
            var rootCommand = new RootCommand("RIX - AI-powered code automation")
            {
                Runs(JobCommand.Build(), parsed => JobCommand.CreateConfig(parsed, fileSystem), JobCommand.ExecuteDefaultAsync),
                Runs(SubmitCommand.Build(), parsed => SubmitCommand.CreateConfig(parsed, fileSystem), SubmitCommand.ExecuteDefaultAsync),
                Runs(CiFailureCommand.Build(), parsed => CiFailureCommand.CreateConfig(parsed, fileSystem), CiFailureCommand.ExecuteDefaultAsync),
                Runs(InitializeCommand.Build(), parsed => InitializeCommand.CreateConfig(parsed, fileSystem), InitializeCommand.ExecuteDefaultAsync),
            };
            return await CliPipeline.InvokeAsync(rootCommand, args, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancelKeyPress;
        }
    }

    /// <summary>Makes invoking <paramref name="command"/> build its config and run it. The build
    /// happens inside the action so an <see cref="InvalidInputException"/> it throws is reported
    /// like any other (see <see cref="CliPipeline.ReportingInvalidInput"/>). Both halves are the
    /// command's own: it turns the parse result into a config and it runs one.</summary>
    private static Command Runs<TConfig>
    (
        Command command,
        Func<ParseResult, TConfig> create,
        Func<TConfig, CancellationToken, Task<int>> execute
    )
    {
        command.SetAction(CliPipeline.ReportingInvalidInput((parsed, cancellationToken) => execute(create(parsed), cancellationToken)));
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
}
