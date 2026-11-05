namespace Rix;

/// <summary>The production <see cref="IFileSystem"/>: the local disk. The one type allowed to touch
/// <see cref="File"/> and <see cref="Directory"/> directly, or to look up where the process, the
/// temp files and the user's home are.</summary>
internal sealed class LocalFileSystem : IFileSystem
{
    public string CurrentDirectory => Directory.GetCurrentDirectory();

    // CreateTempSubdirectory, not a path under GetTempPath() we create ourselves: it makes the
    // directory 0700 on Unix, and on Windows under the user's own profile temp where the inherited
    // ACL grants nobody else access. Either way the contents are the current user's alone, which a
    // directory created in the shared temp root would not be.
    public string CreatePrivateTempDirectory() => Directory.CreateTempSubdirectory("rix-").FullName;

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
