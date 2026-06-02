using Rix.Repository;

namespace Rix.Tests;

internal sealed class StubRepositoryHost(Func<BranchName, Task<bool>>? branchExists = null) : IRepositoryHost
{
    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken) =>
        branchExists is not null ? branchExists(branch) : Task.FromResult(false);
}
