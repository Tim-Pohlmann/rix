namespace Rix;

/// <summary>A uniquely-named working directory under a base dir that recursively deletes itself on
/// dispose. Cleanup is best-effort: any I/O failure (already removed, locked file, denied access) is
/// swallowed so a leftover temp dir never fails the command or masks an earlier exception. Replaces
/// the hand-rolled guid-dir + try/finally cleanup duplicated by the job and submit runners.</summary>
internal sealed class TempDirectory : IDisposable
{
    private readonly IFileSystem _fileSystem;

    internal string Path { get; }

    private TempDirectory(IFileSystem fileSystem, string path)
    {
        _fileSystem = fileSystem;
        Path = path;
    }

    internal static TempDirectory Create(IFileSystem fileSystem, string baseDir, string prefix)
    {
        var path = System.IO.Path.Combine(baseDir, $"{prefix}-{Guid.NewGuid():N}");
        fileSystem.CreateDirectory(path);
        return new TempDirectory(fileSystem, path);
    }

    public void Dispose()
    {
        // DirectoryNotFoundException derives from IOException, so this also covers the
        // already-cleaned-up case.
        try { _fileSystem.DeleteDirectory(Path); }
        catch (IOException) { /* best-effort: leave the temp dir rather than fault */ }
        catch (UnauthorizedAccessException) { /* best-effort: leave the temp dir rather than fault */ }
    }
}
