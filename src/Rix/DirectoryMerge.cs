namespace Rix;

/// <summary>Merges one directory tree into another, keeping whatever already exists at the
/// destination (file, directory or symlink, dangling or not — <see cref="Path.Exists"/> reports all
/// of them), so the runner's own config wins over the factory repo's. Empty source directories are
/// recreated, and symlinks are recreated as symlinks rather than followed: a looping link can't
/// recurse forever and an absolute one can't pull other runner files into the destination.</summary>
internal static class DirectoryMerge
{
    internal static void CopySkippingExisting(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var entry in new DirectoryInfo(sourceDir).EnumerateFileSystemInfos())
        {
            var target = Path.Combine(destDir, entry.Name);
            if (entry is DirectoryInfo && entry.LinkTarget is null)
            {
                if (CanMergeDirectoryInto(target))
                    CopySkippingExisting(entry.FullName, target);
            }
            else if (!Path.Exists(target))
            {
                if (entry.LinkTarget is { } linkTarget)
                    File.CreateSymbolicLink(target, linkTarget);
                else
                    File.Copy(entry.FullName, target);
            }
        }
    }

    /// <summary>Whether a directory can be merged into <paramref name="target"/>: it is already a
    /// directory (or a link to one), whose contents are merged, or nothing is there yet, so it is
    /// created. Anything else in its place - a file, a link to a file or a dangling link - is the
    /// runner's and wins.</summary>
    private static bool CanMergeDirectoryInto(string target)
    => Directory.Exists(target) || !Path.Exists(target);
}
