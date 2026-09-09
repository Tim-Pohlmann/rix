namespace Rix.Initialize;

/// <summary>The outcome of <c>rix initialize</c>: either every template was written, or an I/O
/// error stopped it. Pattern-matched by the shell; never cast.</summary>
internal interface IInitializeResult;

/// <summary>Every caller-workflow template was written. <see cref="WrittenPaths"/> lists them as
/// repo-relative paths (e.g. <c>.github/workflows/rix.yml</c>), in write order.</summary>
internal sealed record InitializeSuccess(IReadOnlyList<string> WrittenPaths) : IInitializeResult;

internal sealed record InitializeFailure(string Message) : IInitializeResult;
