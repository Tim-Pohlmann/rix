using Rix.Agents;
using Rix.Process;
using Rix.Repository;

namespace Rix.Job;

/// <summary>
/// The side-effecting collaborators a job needs, gathered into a single explicit boundary
/// object. The core (<see cref="JobRunner.RunAsync"/>) consumes these; the imperative shell
/// constructs the real implementations at the root of the call stack.
/// </summary>
internal sealed record JobContext
(
    IRepositoryReadHost Host,
    RunProcessAsync RunProcess,
    ICodingAgent Agent,
    LogLine LogLine
);
