using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Runtime.ExceptionServices;

namespace Rix.Cli;

/// <summary>Builds the command-line pipeline every command runs through — the stock defaults
/// (help, version, parse-error reporting, …) plus the one place an <see cref="InvalidInputException"/>
/// is turned into output: the message on stderr and <see cref="ExitCodes.SetupFailed"/>. The
/// commands themselves just construct their config and let a bad flag throw; nothing between here
/// and the value object's constructor needs to know about validation.</summary>
internal static class CliPipeline
{
    internal static Parser Build(RootCommand rootCommand)
    => new CommandLineBuilder(rootCommand)
        .UseDefaults()
        .UseExceptionHandler(ReportInvalidInput)
        .Build();

    /// <summary>Only <see cref="InvalidInputException"/> is a caller mistake worth a one-line
    /// message; anything else is rethrown so the stock handler from <c>UseDefaults</c> reports it
    /// as the unhandled crash it is.</summary>
    private static void ReportInvalidInput(Exception exception, InvocationContext context)
    {
        if (exception is not InvalidInputException)
            ExceptionDispatchInfo.Throw(exception);
        Console.Error.WriteLine($"error: {exception.Message}");
        context.ExitCode = ExitCodes.SetupFailed;
    }
}
