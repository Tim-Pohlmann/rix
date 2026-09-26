namespace Rix.Repository;

/// <summary>Fetches the agent home files with a sparse clone of just the requested directory of the
/// factory repo. <paramref name="gitFor"/> supplies the git client for a repo, since the factory
/// repo is only known per job.</summary>
internal sealed class AgentHomeFetcher(Func<RepoIdentifier, IGit> gitFor) : IAgentHomeFetcher
{
    public async Task<DirectoryPath> FetchAsync
    (
        RepoIdentifier repo, SubDirectoryPath sourcePath, DirectoryPath checkoutDir, CancellationToken cancellationToken
    )
    {
        await gitFor(repo).SparseCloneAsync(checkoutDir.Value, sourcePath, cancellationToken);

        var source = Path.Combine(checkoutDir.Value, sourcePath.Value);
        if (!Directory.Exists(source))
            throw new RepoHostException($"agent home path not found in {repo.Value}: {sourcePath.Value}");
        return new DirectoryPath(source);
    }
}
