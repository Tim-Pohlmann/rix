namespace Rix;

/// <summary>A uniquely-named working directory under a base dir that recursively deletes itself on
/// dispose. Cleanup is best-effort: any I/O failure (already removed, locked file, denied access) is
/// swallowed so a leftover temp dir never fails the command or masks an earlier exception. Replaces
/// the hand-rolled guid-dir + try/finally cleanup duplicated by the job and submit runners.</summary>
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
        // DirectoryNotFoundException derives from IOException, so this also covers the
        // already-cleaned-up case.
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* best-effort: leave the temp dir rather than fault */ }
        catch (UnauthorizedAccessException) { /* best-effort: leave the temp dir rather than fault */ }
    }
}
