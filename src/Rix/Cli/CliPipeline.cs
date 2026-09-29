using System.CommandLine;

namespace Rix.Cli;

/// <summary>How every command line is run: the library's stock help, version and parse-error
/// reporting, plus the one place an <see cref="InvalidInputException"/> is turned into output — the
/// message on stderr and <see cref="ExitCodes.SetupFailed"/>. The commands themselves just construct
/// their config and let a bad flag throw; nothing between here and the value object's constructor
/// needs to know about validation.</summary>
internal static class CliPipeline
{
    /// <summary>Parses <paramref name="args"/> against <paramref name="rootCommand"/> and runs the
    /// chosen command. The library's own Ctrl+C/SIGTERM handling is switched off:
    /// <see cref="Startup.RunAsync"/> installs its own, which lets an in-flight run unwind instead
    /// of being cut off after a fixed grace period.</summary>
    internal static Task<int> InvokeAsync(RootCommand rootCommand, string[] args)
    => rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null });

    /// <summary>Wraps a command's action so an <see cref="InvalidInputException"/> is reported as a
    /// one-line message. Anything else propagates to the library's default exception handler, which
    /// reports it as the unhandled crash it is.</summary>
    internal static Func<ParseResult, Task<int>> ReportingInvalidInput(Func<ParseResult, Task<int>> action)
    => async parsed =>
    {
        try
        {
            return await action(parsed);
        }
        catch (InvalidInputException exception)
        {
            await Console.Error.WriteLineAsync($"error: {exception.Message}");
            return ExitCodes.SetupFailed;
        }
    };
}
