namespace Rix.Repository;

/// <summary>The repository operations <c>rix submit</c> needs against a target repo it can write to:
/// clone (with a write credential), check whether a branch already exists, and open a pull request.
/// Segregated from <see cref="IRepositoryHost"/> so the read-only job path never depends on write
/// capabilities.</summary>
internal interface ISubmitHost
{
    Task CloneAsync(string targetDirectory, CancellationToken cancellationToken);
    Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken);
    Task CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken);
}
