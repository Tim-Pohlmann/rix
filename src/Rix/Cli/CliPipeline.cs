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
    /// chosen command under <paramref name="cancellationToken"/>, which reaches every action.
    /// The library's own Ctrl+C/SIGTERM handling is switched off: <see cref="Startup.RunAsync"/>
    /// installs its own, which lets an in-flight run unwind instead of being cut off after a fixed
    /// grace period.</summary>
    internal static Task<int> InvokeAsync(RootCommand rootCommand, string[] args, CancellationToken cancellationToken)
    => rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration { ProcessTerminationTimeout = null }, cancellationToken);

    /// <summary>Wraps a command's action so an <see cref="InvalidInputException"/> is reported as a
    /// one-line message. Anything else propagates to the library's default exception handler, which
    /// reports it as the unhandled crash it is. Wraps the whole action rather than only the config
    /// read, because the exception also escapes later: <c>submit</c> deserializes a
    /// <c>result.json</c> whose branch names revalidate on the way in.</summary>
    internal static Func<ParseResult, CancellationToken, Task<int>> ReportingInvalidInput(Func<ParseResult, CancellationToken, Task<int>> action)
    => async (parsed, cancellationToken) =>
    {
        try
        {
            return await action(parsed, cancellationToken);
        }
        catch (InvalidInputException exception)
        {
            await Console.Error.WriteLineAsync($"error: {exception.Message}");
            return ExitCodes.SetupFailed;
        }
    };
}
