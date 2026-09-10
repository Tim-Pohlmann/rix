namespace Rix;

/// <summary>Merges one directory tree into another, keeping any file that already exists at the
/// destination — the runner's own config wins over the factory context on a collision. Empty source
/// directories are recreated too, so a deliberately-empty placeholder dir survives the copy.</summary>
internal static class DirectoryMerge
{
    internal static void CopySkippingExisting(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var entry in Directory.EnumerateFileSystemEntries(sourceDir))
        {
            var target = Path.Combine(destDir, Path.GetFileName(entry));
            if (Directory.Exists(entry))
                CopySkippingExisting(entry, target);
            else if (!File.Exists(target))
                File.Copy(entry, target);
        }
    }
}
