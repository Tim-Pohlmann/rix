using Rix.Process;
using Rix.Repository;

namespace Rix.Submit;

/// <summary>
/// The side-effecting collaborators <c>rix submit</c> needs, gathered into a single explicit
/// boundary object. The core (<see cref="SubmitRunner.RunAsync"/>) consumes these; the imperative
/// shell constructs the real implementations at the root of the call stack.
/// </summary>
internal sealed record SubmitContext(
    IRepositoryHost Host,
    RunProcessAsync RunProcess,
    LogLine LogLine);
