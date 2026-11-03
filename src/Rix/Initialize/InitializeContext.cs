namespace Rix.Initialize;

/// <summary>
/// The side-effecting collaborators <c>rix initialize</c> needs, gathered into a single explicit
/// boundary object. The core (<see cref="InitializeRunner.RunAsync"/>) consumes these; the
/// imperative shell constructs the real implementations at the root of the call stack.
/// </summary>
internal sealed record InitializeContext
(
    IFileSystem FileSystem,
    LogLine LogLine
);
