namespace Rix.Repository;

/// <summary>Read-only GitHub Actions operations needed to describe why a run failed: the run's own
/// outcome, its failing jobs' logs, any open PR for its branch, and how much of that branch's tip
/// rix wrote itself. Kept separate from
/// <see cref="IJobRepoHost"/> so <c>rix job</c>'s stub host isn't forced to implement
/// operations it never uses.</summary>
internal interface ICiFailureRepoHost
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

    Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken);

    /// <summary>How many commits at <paramref name="branch"/>'s tip rix authored itself, counting
    /// back from the tip and stopping at the first commit it didn't — so anyone else pushing to the
    /// branch clears the streak, which is what makes a human stepping in enough to re-enable rix.
    /// Never reports more than <paramref name="max"/>: the caller only needs to know whether the
    /// streak reaches its cap, so counting past it would be work no answer depends on.</summary>
    Task<int> CountLeadingRixCommitsAsync(BranchName branch, MaxRixCommits max, CancellationToken cancellationToken);
}

/// <summary>The facts about one workflow run needed to describe why it failed, and to decide
/// whether it may be answered at all. <paramref name="Conclusion"/> is <c>null</c> while the run is
/// still queued/in-progress. <paramref name="HeadRepo"/> is the <c>owner/name</c> of the repo the
/// run's branch lives in, which is the fork rather than the watched repo when the run belongs to a
/// fork's pull request.</summary>
internal sealed record WorkflowRun
(
    string? Conclusion,
    string DisplayTitle,
    string HtmlUrl,
    string HeadBranch,
    string HeadRepo
);
