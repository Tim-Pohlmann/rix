namespace Rix.Process;

/// <summary>The single side-effect seam for running a subprocess. Every part of a job — agent
/// install, the agent run itself, and git operations — flows through one of these so effects stay
/// in one place and can be stubbed in tests.</summary>
internal interface IProcessRunner
{
    /// <summary>Runs <paramref name="fileName"/> to completion and reports how it went.</summary>
    /// <param name="reportOutput">Where each line the child writes to stdout goes as it arrives,
    /// for a caller that wants the run as it happens rather than only its outcome. <c>null</c> for
    /// a caller that doesn't, such as a <c>git</c> call, which needs only whether it worked.</param>
    /// <param name="reportDiagnostic">Where each line the child writes to stderr goes. Not
    /// optional, because this is the only copy: stderr is redirected so the pipe cannot fill and
    /// deadlock the run, so a line nobody is given is a line nobody can see. Separate from
    /// <paramref name="reportOutput"/> so the two streams stay apart without a prefix smuggling the
    /// difference through one sink, and so a caller can treat them differently - the agent's stdout
    /// is parsed for its transcript, its stderr only logged.</param>
    /// <remarks>Both sinks may be invoked from either of the two draining tasks, and the
    /// implementation serializes all calls to both - against each other as well as themselves -
    /// rather than leaving every caller to guard the race. Neither has a default: where a child's
    /// output goes is a decision every call site should be made to state.</remarks>
    Task<ProcessResult> RunAsync
    (
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        Action<string>? reportOutput,
        Action<string> reportDiagnostic,
        CancellationToken cancellationToken
    );
}
