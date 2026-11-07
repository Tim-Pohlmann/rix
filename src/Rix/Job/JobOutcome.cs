using Rix.Artifacts;

namespace Rix.Job;

/// <summary>What a finished run leaves a caller with: the job's own outcome, and whether
/// <c>result.json</c> reached disk.
///
/// Two values rather than a fourth <see cref="IJobResult"/> variant, because they answer different
/// questions and a reader of one needs the other unchanged: <paramref name="Result"/> is what the
/// job did, and it is still correct and still printed when the file is lost, while
/// <paramref name="ResultFile"/> is only about the recording. Folding a lost file into the result
/// would put it on stdout in place of the outcome it failed to record, which is exactly the thing
/// a caller that can no longer read the file still needs.</summary>
internal sealed record JobOutcome(IJobResult Result, IArtifactWrite ResultFile);
