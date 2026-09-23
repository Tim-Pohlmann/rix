namespace Rix.Repository;

/// <summary>Fetches the factory-repo home context with a shallow, blobless, sparse <c>git clone</c>
/// of just the requested directory, through the shared <see cref="GitCli"/> so the read token never
/// lands in argv or a persisted remote URL.</summary>
internal sealed class GitHubFactoryContextLoader(GitCli git) : IFactoryContextLoader
{
    public async Task<string> FetchAsync
    (
        RepoIdentifier repo, RepoRelativePath contextPath, string checkoutDir, CancellationToken cancellationToken
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
            ["sparse-checkout", "set", contextPath.Value],
            workingDirectory: checkoutDir,
            authenticated: true,
            cancellationToken
        );

        var source = Path.Combine(checkoutDir, contextPath.Value);
        if (!Directory.Exists(source))
            throw new RepoHostException($"factory context path not found in {repo.Value}: {contextPath.Value}");
        return source;
    }
}
