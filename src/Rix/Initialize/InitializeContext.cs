namespace Rix.Initialize;

/// <summary>Writes <paramref name="content"/> to <paramref name="path"/>, creating any missing
/// parent directories and overwriting an existing file. The one side effect
/// <see cref="InitializeRunner"/> needs, injected so tests can capture writes instead of touching
/// disk.</summary>
internal delegate Task WriteFileAsync(string path, string content, CancellationToken cancellationToken);

/// <summary>
/// The side-effecting collaborators <c>rix initialize</c> needs, gathered into a single explicit
/// boundary object. The core (<see cref="InitializeRunner.RunAsync"/>) consumes these; the
/// imperative shell constructs the real implementations at the root of the call stack.
/// </summary>
internal sealed record InitializeContext
(
    WriteFileAsync WriteFile,
    LogLine LogLine
);
