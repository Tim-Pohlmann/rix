namespace Rix.Initialize;

/// <summary>Where <c>rix initialize</c> writes the caller workflows: a directory
/// <see cref="Startup"/> checked exists before the command ran, plus the rix ref those workflows
/// call.</summary>
internal sealed record InitializeConfig(DirectoryPath TargetDir, WorkflowRef Ref);
