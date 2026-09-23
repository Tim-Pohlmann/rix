using Rix.Process;

namespace Rix.Repository;

/// <summary>Loads the factory-repo home context by doing a shallow, blobless, sparse <c>git clone</c>
/// of just the requested directory and copying its contents over the runner's user home. Runs git
/// through the shared <see cref="GitCli"/> so the read token never lands in argv or a persisted
/// remote URL.</summary>
internal sealed class GitHubFactoryContextLoader : IFactoryContextLoader
{
    private readonly GitCli _git;
    private readonly string _workDir;
    private readonly string _homeDirectory;

    internal GitHubFactoryContextLoader
    (
        GitReadToken token,
        RunProcessAsync runProcess,
        string workDir,
        string homeDirectory
    )
    {
        _git = new GitCli(token, runProcess);
        _workDir = workDir;
        _homeDirectory = homeDirectory;
    }

    public async Task LoadAsync(RepoIdentifier repo, RepoRelativePath contextPath, CancellationToken cancellationToken)
    {
        // Checked here rather than at construction: every job builds a loader, but only one run with
        // --factory-repo needs a home to copy into.
        if (string.IsNullOrWhiteSpace(_homeDirectory))
            throw new RepoHostException("cannot load factory context: the runner user's home directory could not be determined");

        using var checkout = TempDirectory.Create(_workDir, "rix-factory");

        // Blobless + sparse + depth 1: fetch the commit's tree and only the blobs under the one
        // directory we copy, never the repo's full history or unrelated files. Both steps talk to
        // GitHub: the blobs are only fetched when sparse-checkout populates the working tree, so it
        // needs the credential as much as the clone does.
        await _git.RunAsync
        (
            ["clone", "--depth", "1", "--filter=blob:none", "--sparse",
             $"https://github.com/{repo.Value}.git", checkout.Path],
            workingDirectory: Path.GetTempPath(),
            authenticated: true,
            cancellationToken
        );

        await _git.RunAsync
        (
            ["-C", checkout.Path, "sparse-checkout", "set", contextPath.Value],
            workingDirectory: checkout.Path,
            authenticated: true,
            cancellationToken
        );

        var source = Path.Combine(checkout.Path, contextPath.Value);
        if (!Directory.Exists(source))
            throw new RepoHostException($"factory context path not found in {repo.Value}: {contextPath.Value}");

        try
        {
            DirectoryMerge.CopySkippingExisting(source, _homeDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RepoHostException($"could not copy factory context into {_homeDirectory}: {ex.Message}", ex);
        }
    }
}
