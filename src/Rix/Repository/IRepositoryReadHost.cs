namespace Rix.Repository;

/// <summary>The read-only repository operations <c>rix job</c> needs against a target it only clones
/// and inspects: clone, check whether a branch exists, bundle a branch's commits, and list the
/// pull requests already opened on the remote. Carries no write capability, so the job path can run
/// with a read-only credential. <see cref="IRepositoryHost"/> extends this with the write
/// operations.</summary>
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

    /// <summary>Sets the commit identity inside the already-cloned <paramref name="repoDirectory"/>
    /// (see <see cref="GitIdentity"/>), so the coding agent can commit without guessing author
    /// metadata. Must run after <see cref="CloneAsync"/> and before the agent starts.</summary>
    Task ConfigureGitAsync(string repoDirectory, CancellationToken cancellationToken);

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

    /// <summary>Lists the open pull requests on the remote repo — the "already submitted tasks" the
    /// local API lets the agent review. Read-only, so it works with the job's read credential.</summary>
    Task<IReadOnlyList<RemotePr>> ListOpenPullRequestsAsync(CancellationToken cancellationToken);
}
