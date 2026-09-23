namespace Rix.Repository;

/// <summary>Fetches a directory of configuration/context files from a "factory" repo, so an operator
/// can supply agent CLI config, house style guides, MCP configs and the like from a versioned repo
/// instead of the job prompt. Separate from <see cref="IJobRepoHost"/> because it operates on a
/// different repo than the job's clone target. Only fetches: laying the files over the runner home
/// is the caller's job, so a local copy failure is never reported as a repo-host one.</summary>
internal interface IAgentHomeFetcher
{
    /// <summary>Checks out <paramref name="sourcePath"/> (a repo-relative directory) of
    /// <paramref name="repo"/> into the empty <paramref name="checkoutDir"/> and returns the
    /// directory holding its contents. Throws <see cref="RepoHostException"/> if the repo cannot
    /// be fetched or the path is absent.</summary>
    Task<string> FetchAsync
    (
        RepoIdentifier repo, RepoRelativePath sourcePath, string checkoutDir, CancellationToken cancellationToken
    );
}
