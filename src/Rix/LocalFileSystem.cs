namespace Rix;

/// <summary>The production <see cref="IFileSystem"/>: the local disk. The one type allowed to touch
/// <see cref="File"/> and <see cref="Directory"/> directly, or to look up where the temp files and
/// the user's home are.</summary>
internal sealed class LocalFileSystem : IFileSystem
{
    public string SystemTempDirectory => Path.GetTempPath();

    // On Unix this already consults $HOME before the passwd entry.
    public string UserHomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool PathExists(string path) => Path.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);

    public IEnumerable<FileSystemEntry> EnumerateEntries(string directory)
    => new DirectoryInfo(directory).EnumerateFileSystemInfos().Select(ToEntry);

    public void CopyFile(string source, string destination) => File.Copy(source, destination);

    public void CreateSymbolicLink(string path, string target) => File.CreateSymbolicLink(path, target);

    public Stream OpenRead(string path) => File.OpenRead(path);

    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    private static FileSystemEntry ToEntry(FileSystemInfo info)
    => info switch
    {
        { LinkTarget: { } target } => new SymbolicLinkEntry(info.Name, info.FullName, target),
        DirectoryInfo => new DirectoryEntry(info.Name, info.FullName),
        _ => new FileEntry(info.Name, info.FullName),
    };
}
