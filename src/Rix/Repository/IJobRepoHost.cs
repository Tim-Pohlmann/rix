namespace Rix.Repository;

/// <summary>The repository operations <c>rix job</c> needs against a target it only clones and
/// inspects: clone, check whether a branch exists, and bundle a branch's commits. Carries no write
/// capability, so the job path can run with a read-only credential. <see cref="ISubmitRepoHost"/>
/// extends this with the write operations.
///
/// Named, like every repo host here, for the subcommand that needs it rather than for how much of
/// the repo it may touch — so each subcommand has a config, a context and a repo host that read as
/// one set, and "which command is this for" doesn't compete with "read or write" in the same name.
/// The <c>Repo</c> is what the host is a host of, and what separates these from the two seams they
/// are built out of: <see cref="GitCli"/> runs git, <see cref="GitHubApi"/> speaks REST, and a repo
/// host is the repo-level operation a subcommand actually asks for, whichever of the two it takes
/// to carry it out.</summary>
internal interface IJobRepoHost
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
}
