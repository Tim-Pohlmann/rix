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
        using var checkout = TempDirectory.Create(_workDir, "rix-factory");

        // Blobless + sparse + depth 1: fetch the commit's tree and only the blobs under the one
        // directory we copy, never the repo's full history or unrelated files.
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
            authenticated: false,
            cancellationToken
        );

        var source = Path.Combine(checkout.Path, contextPath.Value);
        if (!Directory.Exists(source))
            throw new InvalidOperationException($"factory context path not found in {repo.Value}: {contextPath.Value}");

        DirectoryMerge.CopySkippingExisting(source, _homeDirectory);
    }
}
