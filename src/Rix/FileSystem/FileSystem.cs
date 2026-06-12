namespace Rix.FileSystem;

/// <summary>The filesystem effects a job needs to provision and tear down its working tree.
/// Injected through <c>JobContext</c> so the core (<c>JobRunner.RunAsync</c>) stays effect-free
/// — like the process, host, and install effects already are — and the clone/cleanup lifecycle
/// is testable without touching a real disk.</summary>
internal interface IFileSystem
{
    /// <summary>Creates <paramref name="path"/> and any missing parent directories.</summary>
    void CreateDirectory(string path);

    /// <summary>Recursively deletes <paramref name="path"/>; a no-op if it no longer exists.</summary>
    void DeleteDirectory(string path);
}

/// <summary>The production <see cref="IFileSystem"/> backed by <see cref="System.IO.Directory"/>.</summary>
internal sealed class LocalFileSystem : IFileSystem
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (DirectoryNotFoundException) { /* already cleaned up */ }
    }
}
