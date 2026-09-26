namespace Rix;

/// <summary>The file-system operations rix performs. The seam all file access goes through, so the
/// code deciding what to read, write or copy stays where it belongs and tests can stand in for the
/// disk — the way <see cref="Repository.IGit"/> stands in for git.</summary>
internal interface IFileSystem
{
    /// <summary>The system's directory for temporary files.</summary>
    string SystemTempDirectory { get; }

    /// <summary>The current user's home directory.</summary>
    string UserHomeDirectory { get; }

    bool FileExists(string path);

    /// <summary>Whether <paramref name="path"/> is a directory or a symlink to one.</summary>
    bool DirectoryExists(string path);

    /// <summary>Whether anything is at <paramref name="path"/>: a file, a directory or a symlink,
    /// dangling or not.</summary>
    bool PathExists(string path);

    /// <summary>Creates <paramref name="path"/> and any missing parents; a no-op if it exists.</summary>
    void CreateDirectory(string path);

    /// <summary>Deletes <paramref name="path"/> and everything under it.</summary>
    void DeleteDirectory(string path);

    /// <summary>The entries directly inside <paramref name="directory"/>.</summary>
    IEnumerable<FileSystemEntry> EnumerateEntries(string directory);

    void CopyFile(string source, string destination);

    void CreateSymbolicLink(string path, string target);

    Stream OpenRead(string path);

    /// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, creating any missing
    /// parent directories and overwriting an existing file.</summary>
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);
}

/// <summary>An entry of a directory, as <see cref="IFileSystem.EnumerateEntries"/> lists it.</summary>
internal abstract record FileSystemEntry(string Name, string FullPath);

internal sealed record FileEntry(string Name, string FullPath) : FileSystemEntry(Name, FullPath);

internal sealed record DirectoryEntry(string Name, string FullPath) : FileSystemEntry(Name, FullPath);

/// <summary>A symlink, whatever it points to (a file, a directory or nothing): reported as the link
/// itself rather than followed.</summary>
internal sealed record SymbolicLinkEntry(string Name, string FullPath, string Target) : FileSystemEntry(Name, FullPath);
