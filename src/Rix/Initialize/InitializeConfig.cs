namespace Rix.Initialize;

/// <summary>Where <c>rix initialize</c> writes the caller workflows: a directory that existed when
/// the <see cref="DirectoryPath"/> was constructed, and which kind of repo they set up, which also
/// carries what fills them in.</summary>
internal sealed record InitializeConfig(DirectoryPath TargetDir, IInitializeSetup Setup);
