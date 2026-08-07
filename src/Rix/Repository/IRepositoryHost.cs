namespace Rix.Repository;

/// <summary>A full repository host: every read operation from <see cref="IRepositoryReadHost"/> plus
/// the write operations <c>rix submit</c> needs against a target it can write to — push a branch,
/// open a pull request, and patch/close the PRs behind already-submitted tasks. Requires a write
/// credential; the read-only job path depends only on the narrower <see cref="IRepositoryReadHost"/>.</summary>
internal interface IRepositoryHost : IRepositoryReadHost
{
    Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken);
    Task<string> CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken);
    Task<string> UpdatePullRequestAsync(PendingTaskUpdate update, CancellationToken cancellationToken);
    Task<string> ClosePullRequestAsync(PendingTaskRevert revert, CancellationToken cancellationToken);
}
