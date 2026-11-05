namespace Rix;

/// <summary>The production <see cref="ILog"/>: stderr, which is where diagnostics belong when
/// stdout carries the result a caller parses.</summary>
internal sealed class StderrLog : ILog
{
    public void WriteLine(string line) => Console.Error.WriteLine(line);
}
