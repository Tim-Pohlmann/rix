namespace Rix;

/// <summary>Merges one directory tree into another, keeping any entry that already exists at the
/// destination — the runner's own config wins over the factory context on a collision, whether the
/// existing entry is a file, a directory or a (possibly dangling) symlink. Empty source directories
/// are recreated too, so a deliberately-empty placeholder dir survives the copy. Symlinks are
/// recreated as symlinks rather than followed, so a link that loops back up the tree can't recurse
/// forever and a link to an absolute path can't pull files from elsewhere on the runner into the
/// destination. (<see cref="Path.Exists"/> reports a dangling link as present, which is what makes
/// it the collision check here.)</summary>
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
                // Merge into an existing directory; a non-directory already there wins.
                if (Directory.Exists(target) || !Path.Exists(target))
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
}
