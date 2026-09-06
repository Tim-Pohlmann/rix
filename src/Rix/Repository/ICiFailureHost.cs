namespace Rix.Repository;

/// <summary>Read-only GitHub Actions operations needed to describe why a run failed: the run's own
/// outcome, its failing jobs' logs, and any open PR for its branch. Kept separate from
/// <see cref="IRepositoryReadHost"/> so <c>rix job</c>'s stub host isn't forced to implement
/// operations it never uses.</summary>
internal interface ICiFailureHost
{
    Task<WorkflowRun> GetRunAsync(long runId, CancellationToken cancellationToken);

    Task<string> GetFailedJobLogsAsync(long runId, CancellationToken cancellationToken);

    Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken);
}

/// <summary>The facts about one workflow run needed to describe why it failed.</summary>
internal sealed record WorkflowRun(string Conclusion, string DisplayTitle, string HtmlUrl, string HeadBranch);
