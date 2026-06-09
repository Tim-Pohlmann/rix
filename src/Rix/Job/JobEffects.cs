using Rix.Claude;
using Rix.Repository;

namespace Rix.Job;

/// <summary>
/// The side-effecting collaborators a job needs, gathered into a single explicit boundary
/// object. The core (<see cref="JobRunner.RunAsync"/>) consumes these; the imperative shell
/// constructs the real implementations at the root of the call stack.
/// </summary>
internal sealed record JobEffects(
    IRepositoryHost Host,
    RunProcessAsync RunProcess,
    Func<CancellationToken, Task<InstallResult>> InstallClaude,
    Action<string> LogLine);
