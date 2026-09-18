namespace Rix.Repository;

/// <summary>A full repository host: every read operation from <see cref="IJobHost"/> plus
/// the write operations <c>rix submit</c> needs against a target it can write to — push a branch and
/// open a pull request. Requires a write credential; the read-only job path depends only on the
/// narrower <see cref="IJobHost"/>.</summary>
internal interface ISubmitHost : IJobHost
{
    Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken);
    Task<string> CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken);
}
