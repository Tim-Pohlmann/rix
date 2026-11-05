namespace Rix;

/// <summary>Where a run's diagnostic lines go: progress ("wrote x", "pushed commits to y"), the
/// agent's forwarded stdout, and warnings about output that could not be written. The seam all of
/// rix's own logging goes through, so a runner reports what it is doing without knowing that the
/// production sink is stderr — the way <see cref="IFileSystem"/> stands in for the disk.
///
/// Diagnostics only. What a command produces for a caller to consume - the result JSON on stdout,
/// the artifacts in the output directory - is not logging and does not come through here.</summary>
internal interface ILog
{
    /// <summary>Writes one line. May be called from arbitrary threads: the agent's stdout and the
    /// local API server's own logging both arrive on threadpool threads.</summary>
    void WriteLine(string line);
}
