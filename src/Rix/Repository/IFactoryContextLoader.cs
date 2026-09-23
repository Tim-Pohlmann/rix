namespace Rix.Repository;

/// <summary>Fetches a directory of configuration/context files from a "factory" repo and lays it
/// over the runner's user home before the coding agent starts, so an operator can supply agent CLI
/// config, house style guides, MCP configs and the like from a versioned repo instead of the job
/// prompt. Separate from <see cref="IJobRepoHost"/> because it operates on a different repo than
/// the job's clone target.</summary>
internal interface IFactoryContextLoader
{
    /// <summary>Fetches <paramref name="contextPath"/> (a repo-relative directory) from
    /// <paramref name="repo"/> and copies its contents into the runner's user home, creating missing
    /// directories and skipping any entry that already exists there. Throws
    /// <see cref="RepoHostException"/> for every expected failure — the repo cannot be fetched, the
    /// path is absent, the home is unknown or the copy hits an I/O error — so the caller can report
    /// it as a setup failure by catching that one type.</summary>
    Task LoadAsync(RepoIdentifier repo, RepoRelativePath contextPath, CancellationToken cancellationToken);
}
