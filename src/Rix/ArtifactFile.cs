namespace Rix;

/// <summary>Writing a file whose content is already settled: a job's <c>result.json</c> and
/// <c>transcript.md</c>, <c>ci-failure</c>'s verdict and prompt. By the time one of these runs the
/// outcome is decided - the agent has run, the money is spent, the bundles exist - so a full disk,
/// an unwritable output directory or a cancellation arriving in that window must not change what
/// is reported. The write therefore neither throws nor honours a
/// <see cref="CancellationToken"/>: the failure is logged, the caller is told, and it carries on
/// with the result it already has.
///
/// What the loss costs is the caller's to say, and the answers differ - nothing beyond that
/// warning for <c>transcript.md</c>, which nothing reads back, and
/// <see cref="ExitCodes.ResultNotWritten"/> for a job's <c>result.json</c>, which <c>rix submit</c>
/// and the job summary both read out of the directory. One place rather than one per writer: how
/// the write is performed is the same whoever is writing, and a second copy of it is a second
/// chance to get the catch list or the warning wrong.</summary>
internal static class ArtifactFile
{
    internal static async Task<IArtifactWrite> WriteAsync
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
            return new ArtifactLost(ex.Message);
        }

        return new ArtifactWritten();
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

/// <summary>Whether a file a command leaves behind reached disk. Reported back rather than only
/// logged, because the same failed write means different things for different files and only the
/// caller knows which file it asked for.</summary>
internal interface IArtifactWrite;

internal sealed record ArtifactWritten : IArtifactWrite;

/// <summary>The file is not there. <paramref name="Error"/> is why, already warned about through
/// the log the write was given.</summary>
internal sealed record ArtifactLost(string Error) : IArtifactWrite;
