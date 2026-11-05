namespace Rix;

/// <summary>Writing a file whose content is already settled: a job's <c>result.json</c> and
/// <c>transcript.md</c>, <c>ci-failure</c>'s verdict. By the time one of these runs the outcome is
/// decided - the agent has run, the money is spent, the bundles exist - so a full disk, an
/// unwritable output directory or a cancellation arriving in that window must not change what is
/// reported. The failure is logged and the caller carries on with the result it already has; only
/// the file is lost, which is why nothing here takes a <see cref="CancellationToken"/>.
///
/// One place rather than one per writer: the rule is the same whoever is writing, and a second copy
/// of it is a second chance to get the catch list or the swallowed warning wrong.</summary>
internal static class BestEffortArtifact
{
    internal static async Task WriteAsync
    (
        IFileSystem fileSystem, ILog log, DirectoryPath directory, string name, string content
    )
    {
        try
        {
            await fileSystem.WriteAllTextAsync
            (
                Path.Combine(directory.Value, name), content, CancellationToken.None
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn(log, $"warning: failed to write {name}: {ex.Message}");
        }
    }

    /// <summary>Reports <paramref name="line"/> through <paramref name="log"/>, swallowing the ways
    /// a closed or broken sink can fail - the warning about a lost file must not cost the correct
    /// result any more than the lost file itself did.</summary>
    private static void Warn(ILog log, string line)
    {
        try { log.WriteLine(line); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* nothing left to report to */ }
    }
}
