namespace Rix.Tests;

/// <summary>Redirects <see cref="Console.Error"/> into a buffer for the scope's lifetime, so a test
/// can assert on what a command reported to the user. Tests run sequentially, so swapping the
/// process-wide writer is safe here.</summary>
internal sealed class ConsoleErrorScope : IDisposable
{
    private readonly TextWriter _original = Console.Error;
    private readonly StringWriter _buffer = new();

    internal ConsoleErrorScope() => Console.SetError(_buffer);

    internal string Text => _buffer.ToString();

    public void Dispose()
    {
        Console.SetError(_original);
        _buffer.Dispose();
    }
}
