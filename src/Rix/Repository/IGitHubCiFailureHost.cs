namespace Rix.Repository;

/// <summary>Read-only GitHub Actions operations needed to describe why a run failed: the run's own
/// outcome, its failing jobs' logs, and any open PR for its branch. Kept separate from
/// <see cref="IRepositoryReadHost"/> so <c>rix job</c>'s stub host isn't forced to implement
/// operations it never uses.</summary>
internal interface IGitHubCiFailureHost
{
    Task<WorkflowRun> GetRunAsync(RunId runId, CancellationToken cancellationToken);

    /// <summary>Concatenates the logs of every job that failed in the run, keeping only the last
    /// <paramref name="tailCharsPerJob"/> characters of each. The caller owns that budget because
    /// it is the one that has to fit the excerpt into a prompt; passing it down means the bytes
    /// beyond it are dropped as they arrive instead of after a whole multi-MB log is in memory.</summary>
    Task<string> GetFailedJobLogsAsync(RunId runId, int tailCharsPerJob, CancellationToken cancellationToken);

    Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken);
}

/// <summary>The facts about one workflow run needed to describe why it failed. <paramref
/// name="Conclusion"/> is <c>null</c> while the run is still queued/in-progress.</summary>
internal sealed record WorkflowRun(string? Conclusion, string DisplayTitle, string HtmlUrl, string HeadBranch);
