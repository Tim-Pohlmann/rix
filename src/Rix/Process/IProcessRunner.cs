namespace Rix.Process;

/// <summary>The single side-effect seam for running a subprocess. Every part of a job — agent
/// install, the agent run itself, and git operations — flows through one of these so effects stay
/// in one place and can be stubbed in tests.</summary>
internal interface IProcessRunner
{
    /// <summary>Runs <paramref name="fileName"/> to completion and reports how it went.</summary>
    /// <param name="onStdoutLine">Called once per output line as it arrives, from stdout and
    /// stderr both. May be invoked from either of the two draining tasks, so the implementation
    /// serializes the calls rather than leaving every caller to guard the race.</param>
    Task<ProcessResult> RunAsync
    (
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentOverrides = null,
        Action<string>? onStdoutLine = null,
        CancellationToken cancellationToken = default
    );
}
