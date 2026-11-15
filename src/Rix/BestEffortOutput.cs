using Rix.Artifacts;

namespace Rix;

/// <summary>A run's result line for a run that has already recorded it elsewhere: the result
/// JSON, so a caller watching the command sees the same verdict that went to disk. Reports
/// a failure instead of raising it - a broken pipe in the window after the work finished must not
/// stop the correct exit code from being returned. Only the line is lost.
///
/// Files don't come through here, whether they are an agent run's artifacts or <c>ci-failure</c>'s
/// verdict: <see cref="ArtifactFile"/> writes those, and what a lost one costs is decided per
/// file there rather than always being nothing.</summary>
internal static class BestEffortOutput
{
    internal static void WriteResult(ILog log, string line)
    {
        try { log.WriteResult(line); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* nothing left to report to */ }
    }
}
