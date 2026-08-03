namespace Rix.Repository;

/// <summary>The read-only repository operations <c>rix job</c> needs against a target it only clones
/// and inspects: clone, check whether a branch exists, and bundle a branch's commits. Carries no
/// write capability, so the job path can run with a read-only credential. <see cref="IRepositoryHost"/>
/// extends this with the write operations.</summary>
internal interface IRepositoryReadHost
{
    Task CloneAsync(string targetDirectory, CancellationToken cancellationToken);
    Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken);

    /// <summary>Checks whether <paramref name="branch"/> exists as a local ref inside the
    /// already-cloned <paramref name="repoDirectory"/>. Used to catch an agent that reports a branch
    /// via the local PR API without having actually committed it into its assigned working
    /// directory (e.g. because it made changes in a different directory on the runner) — the
    /// mistake is caught immediately, instead of surfacing later as an opaque git-bundle failure.</summary>
    Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken);

    /// <summary>Bundles the commits on <paramref name="branch"/> not on <paramref name="baseBranch"/>
    /// into a git bundle at <paramref name="bundlePath"/>, run inside the cloned
    /// <paramref name="repoDirectory"/>.</summary>
    Task CreateBundleAsync
    (
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken
    );
}
