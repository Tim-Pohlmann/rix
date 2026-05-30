namespace Rix.Tests;

internal sealed class EnvScope : IDisposable
{
    private readonly Dictionary<string, string?> _originals = [];

    internal void Set(string key, string? value)
    {
        if (!_originals.ContainsKey(key))
            _originals[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    public void Dispose()
    {
        foreach (var (key, value) in _originals)
            Environment.SetEnvironmentVariable(key, value);
    }
}
