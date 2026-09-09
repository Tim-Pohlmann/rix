using Rix.Process;

namespace Rix.Repository;

/// <summary>Loads the factory-repo home context by doing a shallow, blobless, sparse <c>git clone</c>
/// of just the requested directory and copying its contents over the runner's user home. Reuses the
/// same credential injection as <see cref="GitHubReadHost"/> (<see cref="GitHubAuth"/>) so the read
/// token never lands in argv or a persisted remote URL.</summary>
internal sealed class GitHubFactoryContextLoader : IFactoryContextLoader
{
    private readonly IReadOnlyDictionary<string, string> _gitAuthEnv;
    private readonly RunProcessAsync _runProcess;
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
        _gitAuthEnv = GitHubAuth.ExtraHeaderEnv(token);
        _runProcess = runProcess;
        _workDir = workDir;
        _homeDirectory = homeDirectory;
    }

    public async Task LoadAsync(RepoIdentifier repo, RepoRelativePath contextPath, CancellationToken cancellationToken)
    {
        using var checkout = TempDirectory.Create(_workDir, "rix-factory");

        // Blobless + sparse + depth 1: fetch the commit's tree and only the blobs under the one
        // directory we copy, never the repo's full history or unrelated files.
        await RunGitAsync
        (
            ["clone", "--depth", "1", "--filter=blob:none", "--sparse",
             $"https://github.com/{repo.Value}.git", checkout.Path],
            workingDirectory: Path.GetTempPath(),
            authenticated: true,
            cancellationToken
        );

        await RunGitAsync
        (
            ["-C", checkout.Path, "sparse-checkout", "set", contextPath.Value],
            workingDirectory: checkout.Path,
            authenticated: false,
            cancellationToken
        );

        var source = Path.Combine(checkout.Path, contextPath.Value);
        if (!Directory.Exists(source))
            throw new InvalidOperationException($"factory context path not found in {repo.Value}: {contextPath.Value}");

        CopySkippingExisting(source, _homeDirectory);
    }

    /// <summary>Recursively copies <paramref name="sourceDir"/> into <paramref name="destDir"/>:
    /// every source subdirectory is created (so empty ones survive) and every file is copied unless
    /// a file already exists at the destination, which is left untouched — the runner's own config
    /// wins over the factory context on a collision.</summary>
    private static void CopySkippingExisting(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destDir, Path.GetRelativePath(sourceDir, dir)));

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destDir, Path.GetRelativePath(sourceDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target))
                File.Copy(file, target);
        }
    }

    /// <summary>Runs <c>git</c> through the process seam, injecting the credential env only for
    /// <paramref name="authenticated"/> (remote) commands, and turns a non-zero exit into an
    /// <see cref="InvalidOperationException"/> so <see cref="LoadAsync"/>'s caller maps it to a
    /// setup failure. Mirrors <see cref="GitHubReadHost.RunGitAsync"/>.</summary>
    private async Task RunGitAsync
    (
        string[] args, string workingDirectory, bool authenticated, CancellationToken cancellationToken
    )
    {
        var env = authenticated switch
        {
            true => (IReadOnlyDictionary<string, string>?)_gitAuthEnv,
            false => null,
        };
        var result = await _runProcess("git", args, workingDirectory, env, null, cancellationToken);
        if (result is ProcessFailure f)
            throw new InvalidOperationException($"git {args[0]} failed: {f.Reason}");
    }
}
