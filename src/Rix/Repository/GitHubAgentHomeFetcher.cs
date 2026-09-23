namespace Rix.Repository;

/// <summary>Fetches the agent home files from the factory repo with a shallow, blobless, sparse
/// <c>git clone</c> of just the requested directory, authenticated through the same
/// <see cref="GitCli"/> as the job clone.</summary>
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
            ["clone", "--depth", "1", "--filter=blob:none", "--sparse", $"https://github.com/{repo.Value}.git", checkoutDir],
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
