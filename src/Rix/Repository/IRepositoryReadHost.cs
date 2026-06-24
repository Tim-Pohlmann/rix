namespace Rix.Repository;

/// <summary>The read-only repository operations <c>rix job</c> needs against a target it only clones
/// and inspects: clone, check whether a branch exists, and bundle a branch's commits. Carries no
/// write capability, so the job path can run with a read-only credential. <see cref="IRepositoryHost"/>
/// extends this with the write operations.</summary>
internal interface IRepositoryReadHost
{
    Task CloneAsync(string targetDirectory, CancellationToken cancellationToken);
    Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken);

    /// <summary>Bundles the commits on <paramref name="branch"/> not on <paramref name="baseBranch"/>
    /// into a git bundle at <paramref name="bundlePath"/>, run inside the cloned
    /// <paramref name="repoDirectory"/>.</summary>
    Task CreateBundleAsync(
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken);
}
