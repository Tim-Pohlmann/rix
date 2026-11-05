namespace Rix.Cli;

/// <summary>The lines a command writes to a console stream once its outcome is already decided:
/// the result JSON to stdout, so a caller watching the command sees the same verdict that went to
/// disk. Reports a failure instead of raising it and takes no
/// <see cref="CancellationToken"/> - a broken pipe, or a cancellation arriving in the window after
/// the work finished, must not stop the correct exit code from being returned. Only the line is
/// lost.
///
/// Files don't come through here, whether they are a job's artifacts or <c>ci-failure</c>'s
/// verdict: <see cref="BestEffortArtifact"/> writes those, under the same rule.</summary>
internal static class BestEffortOutput
{
    internal static async Task WriteLineAsync(TextWriter writer, string line)
    {
        try { await writer.WriteLineAsync(line); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* nothing left to report to */ }
    }
}
