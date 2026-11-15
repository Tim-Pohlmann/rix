namespace Rix;

/// <summary>Where a command's console output goes, so neither a runner nor a command knows that
/// the production sinks are stderr and stdout — the way <see cref="IFileSystem"/> stands in for the
/// disk. Two streams, because a caller reads them differently: the diagnostics are for whoever
/// watches the run, and the result is for whatever parses it, which only works if nothing else is
/// mixed in.
///
/// Files are not output in this sense: the artifacts in the output directory are written by
/// <see cref="Artifacts.ArtifactFile"/>.</summary>
internal interface ILog
{
    /// <summary>Writes one diagnostic line: progress ("wrote x", "pushed commits to y"), the agent's
    /// forwarded stdout, warnings about output that could not be written, and what a person has to
    /// do next. May be called from arbitrary threads: the agent's stdout and the local API server's
    /// own logging both arrive on threadpool threads.</summary>
    void WriteLine(string line);

    /// <summary>Writes the command's result, once its outcome is decided.</summary>
    void WriteResult(string line);
}
