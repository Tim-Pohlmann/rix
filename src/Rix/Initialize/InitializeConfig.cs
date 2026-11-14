namespace Rix.Initialize;

/// <summary>Where <c>rix initialize</c> writes the caller workflows: a directory that existed when
/// the <see cref="DirectoryPath"/> was constructed, the rix ref those workflows call, and which
/// kind of repo they set up.</summary>
internal sealed record InitializeConfig(DirectoryPath TargetDir, WorkflowRef Ref, IInitializeSetup Setup);
