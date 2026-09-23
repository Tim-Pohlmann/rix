namespace Rix.Repository;

/// <summary>Fetches the agent home files from the factory repo with a shallow, blobless, sparse <c>git clone</c>
/// of just the requested directory, through the shared <see cref="GitCli"/> so the read token never
/// lands in argv or a persisted remote URL.</summary>
internal sealed class GitHubAgentHomeFetcher(GitCli git) : IAgentHomeFetcher
{
    public async Task<string> FetchAsync
    (
        RepoIdentifier repo, RepoRelativePath sourcePath, string checkoutDir, CancellationToken cancellationToken
    )
    {
        // Blobless + sparse + depth 1: fetch the commit's tree and only the blobs under the one
        // directory we copy, never the repo's full history or unrelated files. Both steps talk to
        // GitHub: the blobs are only fetched when sparse-checkout populates the working tree, so it
        // needs the credential as much as the clone does.
        await git.RunAsync
        (
            ["clone", "--depth", "1", "--filter=blob:none", "--sparse", GitCli.CloneUrl(repo), checkoutDir],
            workingDirectory: Path.GetTempPath(),
            authenticated: true,
            cancellationToken
        );

        await git.RunAsync
        (
            ["sparse-checkout", "set", sourcePath.Value],
            workingDirectory: checkoutDir,
            authenticated: true,
            cancellationToken
        );

        var source = Path.Combine(checkoutDir, sourcePath.Value);
        if (!Directory.Exists(source))
            throw new RepoHostException($"agent home path not found in {repo.Value}: {sourcePath.Value}");
        return source;
    }
}
