namespace Rix.Cli;

/// <summary>The writes a command makes once its outcome is already decided: the result JSON to
/// stdout, <c>result.json</c> and friends to the output directory. Every one of them reports a
/// failure instead of raising it, and none of them takes a <see cref="CancellationToken"/> - a
/// broken pipe, a full disk, or a cancellation arriving in the window after the work finished must
/// not stop the correct exit code from being returned. Only the output is lost.
///
/// Takes the one write it performs rather than a file system, so a command still never chooses or
/// holds the disk - it passes on the write its context was built with.</summary>
internal static class BestEffortOutput
{
    internal static async Task WriteAsync(WriteFileAsync write, DirectoryPath outputDir, string name, string content)
    {
        try
        {
            await write(Path.Combine(outputDir.Value, name), content, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Also best-effort: a closed/broken stderr must not defeat the exit-code guarantee.
            await WriteLineAsync(Console.Error, $"warning: failed to write {name}: {ex.Message}");
        }
    }

    internal static async Task WriteLineAsync(TextWriter writer, string line)
    {
        try { await writer.WriteLineAsync(line); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* nothing left to report to */ }
    }
}
