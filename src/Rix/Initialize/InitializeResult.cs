namespace Rix.Initialize;

/// <summary>The outcome of <c>rix initialize</c>: either every template was written, or an I/O
/// error stopped it. Pattern-matched by the shell; never cast.</summary>
internal interface IInitializeResult;

/// <summary>Every caller-workflow template was written. The runner logs each path as it goes, so
/// this carries no payload.</summary>
internal sealed record InitializeSuccess : IInitializeResult;

internal sealed record InitializeFailure(string Message) : IInitializeResult;
