using Rix.Claude;
using Rix.Repository;

namespace Rix.Job;

/// <summary>Installs Claude (or confirms it is present), returning the outcome.</summary>
internal delegate Task<InstallResult> InstallClaudeAsync(CancellationToken cancellationToken);

/// <summary>Writes a single diagnostic line (e.g. a forwarded Claude stdout line) to the log sink.</summary>
internal delegate void LogLine(string line);

/// <summary>
/// The side-effecting collaborators a job needs, gathered into a single explicit boundary
/// object. The core (<see cref="JobRunner.RunAsync"/>) consumes these; the imperative shell
/// constructs the real implementations at the root of the call stack.
/// </summary>
internal sealed record JobContext(
    IRepositoryHost Host,
    RunProcessAsync RunProcess,
    InstallClaudeAsync InstallClaude,
    LogLine LogLine);
