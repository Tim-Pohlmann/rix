namespace Rix.Job;

/// <summary>Pure naming of the git bundle file produced for a branch.</summary>
internal static class BundleFile
{
    /// <summary>
    /// The bundle file name for <paramref name="branch"/>: URL-escaped so it is a safe
    /// single path segment, with '%' replaced by '_' to keep the name human-readable.
    /// </summary>
    internal static string ForBranch(RixBranchName branch) =>
        $"{Uri.EscapeDataString(branch.Value).Replace('%', '_')}.bundle";
}
