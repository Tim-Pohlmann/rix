namespace Rix.Repository;

/// <summary>The git operations rix performs: against a repo on the one host the instance was
/// created for (clone, the remote branch check), and inside the local clones made from it (push goes
/// to the clone's <c>origin</c>). Which repo is a per-call value, since one credential reaches every
/// repo it grants - a job reads both its target and the factory repo with the same token. The seam every
/// git command goes through, so tests can stand in for git without a real repository or remote.
/// Pushing only succeeds with a write-capable token behind it, which only <c>rix submit</c> is
/// given; the job path runs with a read token, so a push from it would be refused by the remote.</summary>
internal interface IGit
{
    /// <summary>Clones <paramref name="repo"/> into <paramref name="targetDirectory"/>.</summary>
    Task CloneAsync(RepoIdentifier repo, string targetDirectory, CancellationToken cancellationToken);

    /// <summary>Clones <paramref name="repo"/> into <paramref name="targetDirectory"/> with only
    /// <paramref name="directory"/> checked out: the latest commit alone, and only the file contents
    /// under that directory.</summary>
    Task SparseCloneAsync
    (
        RepoIdentifier repo, string targetDirectory, SubDirectoryPath directory, CancellationToken cancellationToken
    );

    Task<bool> BranchExistsOnRemoteAsync(RepoIdentifier repo, BranchName branch, CancellationToken cancellationToken);

    /// <summary>Checks whether <paramref name="branch"/> exists as a local ref inside the
    /// already-cloned <paramref name="repoDirectory"/>. Used to catch an agent that reports a branch
    /// via the local PR API without having actually committed it into its assigned working
    /// directory (e.g. because it made changes in a different directory on the runner) — the
    /// mistake is caught immediately, instead of surfacing later as an opaque git-bundle failure.</summary>
    Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken);

    /// <summary>Sets the commit identity inside the already-cloned <paramref name="repoDirectory"/>
    /// (see <see cref="GitIdentity"/>), so the coding agent can commit without guessing author
    /// metadata. Must run after <see cref="CloneAsync"/> and before the agent starts.</summary>
    Task ConfigureIdentityAsync(string repoDirectory, CancellationToken cancellationToken);

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

    /// <summary>Fetches <paramref name="branch"/> out of the git bundle at
    /// <paramref name="bundlePath"/> into a local branch of the same name, inside the already-cloned
    /// <paramref name="repoDirectory"/> - the counterpart of <see cref="CreateBundleAsync"/>.</summary>
    Task FetchBundleAsync(string repoDirectory, string bundlePath, BranchName branch, CancellationToken cancellationToken);

    Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken);
}
