namespace Rix;

/// <summary>The production <see cref="ILog"/>: the result on stdout, where a caller reads it, and
/// diagnostics on stderr, so they never land in the middle of it.</summary>
internal sealed class ConsoleLog : ILog
{
    public void WriteLine(string line) => Console.Error.WriteLine(line);

    public void WriteResult(string line) => Console.Out.WriteLine(line);
}
