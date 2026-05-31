namespace Rix.Repository;

internal interface IRepositoryHost
{
    Task CloneAsync(string targetDirectory, CancellationToken cancellationToken);
    Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken);
    Task PushBranchAsync(BranchName branch, CancellationToken cancellationToken);
    Task<string> CreatePullRequestAsync(BranchName branch, string title, string body, string baseBranch, CancellationToken cancellationToken);
}
