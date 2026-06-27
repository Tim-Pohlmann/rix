namespace Rix;

/// <summary>A uniquely-named working directory under a base dir that deletes itself (recursively,
/// best-effort) on dispose. Replaces the hand-rolled guid-dir + try/finally cleanup duplicated by
/// the job and submit runners.</summary>
internal sealed class TempDirectory : IDisposable
{
    internal string Path { get; }

    private TempDirectory(string path) => Path = path;

    internal static TempDirectory Create(string baseDir, string prefix)
    {
        var path = System.IO.Path.Combine(baseDir, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (DirectoryNotFoundException) { /* already cleaned up */ }
    }
}
