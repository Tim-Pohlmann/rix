namespace Rix.Repository;

/// <summary>The read-only operations against whatever ran the build: what one run's outcome was,
/// and what its failing jobs logged. Separate from <see cref="ICiFailureRepoHost"/> because who
/// hosts the repo and who runs the CI are independently chosen in practice — GitHub repos built by
/// Buildkite, CircleCI or Jenkins are ordinary setups — so a second CI provider should be one new
/// implementation of this, not a second copy of every repo operation alongside it.
///
/// Stated in the vocabulary every CI system shares rather than in any one's: a run, its outcome,
/// the logs of the jobs that failed. What a provider calls those, and which REST fields they arrive
/// in, stops at its implementation.</summary>
internal interface ICiHost
{
    Task<WorkflowRun> GetRunAsync(RunId runId, CancellationToken cancellationToken);

    /// <summary>Builds an excerpt of the run's failed jobs' logs, each job's tail under a heading
    /// naming it, in at most <paramref name="totalTailChars"/> characters however many jobs failed.
    /// The caller owns that budget because it is the one that has to fit the excerpt into a prompt,
    /// and owns it in full rather than per job so the size it asks for is the size it gets; passing
    /// it down means the bytes beyond it are dropped as they arrive instead of after a whole
    /// multi-MB log is in memory. How the budget is divided, and how many jobs are worth covering
    /// before each share is too small to read, is the implementation's call.</summary>
    Task<string> GetFailedJobLogsAsync(RunId runId, int totalTailChars, CancellationToken cancellationToken);
}

/// <summary>The facts about one CI run needed to describe why it failed, and to decide whether it
/// may be answered at all. <paramref name="Conclusion"/> is <c>null</c> while the run is still
/// queued/in-progress. <paramref name="HeadRepo"/> is the <c>owner/name</c> of the repo the run's
/// branch lives in, which is the fork rather than the watched repo when the run belongs to a fork's
/// pull request.</summary>
internal sealed record WorkflowRun
(
    string? Conclusion,
    string DisplayTitle,
    string HtmlUrl,
    string HeadBranch,
    string HeadRepo
);
