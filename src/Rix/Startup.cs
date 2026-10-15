using Rix.Cli;
using System.CommandLine;
using System.Runtime.InteropServices;

namespace Rix;

/// <summary>The process: the signals that cancel a run, the set of commands rix offers, and the
/// invocation. What each command then does is the command's own (see <see cref="Rix.Cli"/>), and
/// what it runs against is <see cref="Composition"/>'s.
///
/// What stays here is what a command cannot do for itself, because it happens before the command's
/// context exists and so before it has a file system: reading a <c>--prompt-file</c> into the text
/// the config takes, and checking that the directories a config names are there.</summary>
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
            var temp = fileSystem.SystemTempDirectory;
            var home = fileSystem.UserHomeDirectory;
            var rootCommand = new RootCommand("RIX - AI-powered code automation")
            {
                Runs
                (
                    fileSystem,
                    JobCommand.Build(),
                    parsed => JobCommand.ReadConfig(parsed, temp, home, PromptText(fileSystem, JobOptions.ReadPrompt(parsed))),
                    JobCommand.RequiredDirectories,
                    (config, cancellationToken) => JobCommand.ExecuteDefaultAsync(config, cancellationToken)
                ),
                Runs
                (
                    fileSystem,
                    SubmitCommand.Build(),
                    parsed => SubmitCommand.ReadConfig(parsed, temp),
                    SubmitCommand.RequiredDirectories,
                    (config, cancellationToken) => SubmitCommand.ExecuteDefaultAsync(config, cancellationToken)
                ),
                Runs
                (
                    fileSystem,
                    CiFailureCommand.Build(),
                    CiFailureCommand.ReadConfig,
                    CiFailureCommand.RequiredDirectories,
                    (config, cancellationToken) => CiFailureCommand.ExecuteDefaultAsync(config, cancellationToken)
                ),
                Runs
                (
                    fileSystem,
                    InitializeCommand.Build(),
                    InitializeCommand.ReadConfig,
                    InitializeCommand.RequiredDirectories,
                    (config, cancellationToken) => InitializeCommand.ExecuteDefaultAsync(config, cancellationToken)
                ),
            };
            return await CliPipeline.InvokeAsync(rootCommand, args, cts.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancelKeyPress;
        }
    }

    /// <summary>Makes invoking <paramref name="command"/> read its config, check the directories it
    /// names exist, and hand both to <paramref name="execute"/>. All three happen inside the action
    /// so an <see cref="InvalidInputException"/> any of them throws is reported like any other (see
    /// <see cref="CliPipeline.ReportingInvalidInput"/>). The command owns <paramref name="execute"/>;
    /// what it needs the file system for is the two steps before it.</summary>
    private static Command Runs<TConfig>
    (
        IFileSystem fileSystem,
        Command command,
        Func<ParseResult, TConfig> read,
        Func<TConfig, IReadOnlyList<RequiredDirectory>> requiredDirectories,
        Func<TConfig, CancellationToken, Task<int>> execute
    )
    {
        var run = WhenDirectoriesExist(fileSystem, requiredDirectories, execute);
        command.SetAction(CliPipeline.ReportingInvalidInput((parsed, cancellationToken) => run(read(parsed), cancellationToken)));
        return command;
    }

    /// <summary>The text <paramref name="prompt"/> gives, reading it from its file if it names one:
    /// the one flag whose value <c>job</c> can't produce itself, since the command never sees the
    /// file system. The file's content is taken whole and unmodified - no trimming, since the
    /// prompt is the text an agent is handed. A missing or blank file is reported under
    /// <c>--prompt-file</c> the way a blank <c>--prompt</c> is: naming it was the caller asking for
    /// a prompt from it, so finding none there is a problem to report rather than a reason to run
    /// the agent on nothing.</summary>
    internal static string PromptText(IFileSystem fileSystem, PromptSource prompt)
    => prompt switch
    {
        PromptText text => text.Text,
        PromptFile file => Input.Named(JobOptions.PromptFileOption.Name, () => ReadPromptFile(fileSystem, file.Path)),
        _ => throw new NotSupportedException($"Unexpected prompt source: {prompt.GetType()}"),
    };

    private static string ReadPromptFile(IFileSystem fileSystem, string path)
    {
        if (!fileSystem.FileExists(path))
            throw new InvalidInputException($"no file at '{path}'");

        using var reader = new StreamReader(fileSystem.OpenRead(path));
        var prompt = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidInputException($"'{path}' holds no prompt");

        return prompt;
    }

    /// <summary>Runs <paramref name="run"/> once every directory <paramref name="requiredDirectories"/>
    /// lists for the config exists: the check the commands leave to the caller, since they never see
    /// the file system. A missing one is an <see cref="InvalidInputException"/>, reported like any
    /// other bad flag.</summary>
    internal static Func<TConfig, CancellationToken, Task<int>> WhenDirectoriesExist<TConfig>
    (
        IFileSystem fileSystem,
        Func<TConfig, IReadOnlyList<RequiredDirectory>> requiredDirectories,
        Func<TConfig, CancellationToken, Task<int>> run
    )
    => (config, cancellationToken) =>
    {
        var missing = requiredDirectories(config).FirstOrDefault(directory => !fileSystem.DirectoryExists(directory.Path.Value));
        if (missing is not null)
            throw new InvalidInputException($"{missing.Name}: directory does not exist: {missing.Path}");
        return run(config, cancellationToken);
    };

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
