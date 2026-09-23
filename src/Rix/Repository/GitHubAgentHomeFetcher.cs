namespace Rix.Repository;

/// <summary>Fetches the agent home files from the factory repo with a shallow, blobless, sparse
/// <c>git clone</c> of just the requested directory.</summary>
internal sealed class GitHubAgentHomeFetcher(GitCli git) : IAgentHomeFetcher
{
    public async Task<DirectoryPath> FetchAsync
    (
        RepoIdentifier repo, SubDirectoryPath sourcePath, DirectoryPath checkoutDir, CancellationToken cancellationToken
    )
    {
        // Blobless + sparse + depth 1: fetch the commit's tree and only the blobs under the one
        // directory we copy, never the repo's full history or unrelated files. Both steps talk to
        // GitHub: the blobs are only fetched when sparse-checkout populates the working tree, so it
        // needs the credential as much as the clone does.
        await git.RunAsync
        (
            ["clone", "--depth", "1", "--filter=blob:none", "--sparse", $"https://github.com/{repo.Value}.git", checkoutDir.Value],
            workingDirectory: Path.GetTempPath(),
            authenticated: true,
            cancellationToken
        );

        await git.RunAsync
        (
            ["sparse-checkout", "set", sourcePath.Value],
            workingDirectory: checkoutDir.Value,
            authenticated: true,
            cancellationToken
        );

        var source = Path.Combine(checkoutDir.Value, sourcePath.Value);
        if (!Directory.Exists(source))
            throw new RepoHostException($"agent home path not found in {repo.Value}: {sourcePath.Value}");
        return new DirectoryPath(source);
    }
}
