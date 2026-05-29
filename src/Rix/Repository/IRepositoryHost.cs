namespace Rix.Repository;

internal interface IRepositoryHost
{
    Task CloneAsync(string targetDirectory, CancellationToken cancellationToken);
    Task<bool> BranchExistsOnRemoteAsync(string branch, CancellationToken cancellationToken);
    Task PushBranchAsync(string branch, CancellationToken cancellationToken);
    Task<string> CreatePullRequestAsync(string branch, string title, string body, CancellationToken cancellationToken);
}
