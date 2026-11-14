using Rix.CodingAgents;
using Rix.Process;
using Rix.Repository;

namespace Rix.Agent;

/// <summary>
/// The side-effecting collaborators a run needs, gathered into a single explicit boundary
/// object. The core (<see cref="AgentRunner.RunAsync"/>) consumes these; the imperative shell
/// constructs the real implementations at the root of the call stack.
/// </summary>
internal sealed record AgentContext
(
    IGit Git,
    IProcessRunner ProcessRunner,
    ICodingAgent Agent,
    ILog Log,
    IFileSystem FileSystem
);
