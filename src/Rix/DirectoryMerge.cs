namespace Rix;

/// <summary>Merges one directory tree into another, keeping whatever already exists at the
/// destination (file, directory or symlink, dangling or not — <see cref="IFileSystem.PathExists"/>
/// reports all of them), so the runner's own config wins over the factory repo's. Empty source
/// directories are recreated, and symlinks are recreated as symlinks rather than followed: a looping
/// link can't recurse forever and an absolute one can't pull other runner files into the
/// destination.</summary>
internal static class DirectoryMerge
{
    internal static void CopySkippingExisting(IFileSystem fileSystem, string sourceDir, string destDir)
    {
        fileSystem.CreateDirectory(destDir);
        foreach (var entry in fileSystem.EnumerateEntries(sourceDir))
        {
            var target = Path.Combine(destDir, entry.Name);
            switch (entry)
            {
                case DirectoryEntry when CanMergeDirectoryInto(fileSystem, target):
                    CopySkippingExisting(fileSystem, entry.FullPath, target);
                    break;
                case DirectoryEntry:
                    break;
                case SymbolicLinkEntry link when !fileSystem.PathExists(target):
                    fileSystem.CreateSymbolicLink(target, link.Target);
                    break;
                case FileEntry when !fileSystem.PathExists(target):
                    fileSystem.CopyFile(entry.FullPath, target);
                    break;
            }
        }
    }

    /// <summary>Whether a directory can be merged into <paramref name="target"/>: it is already a
    /// directory (or a link to one), whose contents are merged, or nothing is there yet, so it is
    /// created. Anything else in its place - a file, a link to a file or a dangling link - is the
    /// runner's and wins.</summary>
    private static bool CanMergeDirectoryInto(IFileSystem fileSystem, string target)
    => fileSystem.DirectoryExists(target) || !fileSystem.PathExists(target);
}
