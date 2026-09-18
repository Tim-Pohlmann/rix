namespace Rix.Initialize;

/// <summary>Where <c>rix initialize</c> writes the caller workflows: a directory that existed when
/// the <see cref="DirectoryPath"/> was constructed.</summary>
internal sealed record InitializeConfig(DirectoryPath TargetDir);
