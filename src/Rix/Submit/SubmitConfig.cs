namespace Rix.Submit;

internal record SubmitConfig
{
    internal RepoIdentifier Repo { get; }
    internal GitToken WriteToken { get; }
    internal DirectoryPath InputDir { get; }
    internal DirectoryPath WorkDir { get; }

    /// <summary>Private so a <see cref="SubmitConfig"/> can only be produced by <see cref="Create"/>,
    /// which guarantees every field is validated — the type can never exist in an invalid state.</summary>
    private SubmitConfig(RepoIdentifier repo, GitToken writeToken, DirectoryPath inputDir, DirectoryPath workDir)
    {
        Repo = repo;
        WriteToken = writeToken;
        InputDir = inputDir;
        WorkDir = workDir;
    }

    /// <summary>Validates and transforms raw CLI/environment inputs into a strongly-typed
    /// <see cref="SubmitConfig"/>, collecting every error in one pass so a <see cref="SubmitConfigValid"/>
    /// is produced only when the whole configuration is well-formed.</summary>
    internal static SubmitConfigResult Create(
        string repo,
        string writeToken,
        string? inputDir,
        string? workDir)
    {
        var errors = new List<string>();

        RepoIdentifier? parsedRepo = null;
        if (string.IsNullOrWhiteSpace(repo))
            errors.Add("--repo is required");
        else
            parsedRepo = RepoIdentifier.Parse(repo).Collect(errors, "--repo");

        if (string.IsNullOrWhiteSpace(writeToken))
            errors.Add("--write-token is required");

        DirectoryPath? parsedInputDir = null;
        if (string.IsNullOrWhiteSpace(inputDir))
            errors.Add("--input-dir is required");
        else
            parsedInputDir = DirectoryPath.Parse(inputDir).Collect(errors, "--input-dir");

        var resolvedWorkDir = string.IsNullOrWhiteSpace(workDir) ? Path.GetTempPath() : workDir;
        var parsedWorkDir = DirectoryPath.Parse(resolvedWorkDir).Collect(errors, "--work-dir");

        if (errors.Count > 0)
            return new SubmitConfigInvalid([.. errors]);

        // Non-null here: any blank or unparseable input would have added an error above.
        return new SubmitConfigValid(new SubmitConfig(
            repo: parsedRepo!,
            writeToken: new GitToken(writeToken),
            inputDir: parsedInputDir!,
            workDir: parsedWorkDir!));
    }
}

/// <summary>The result of <see cref="SubmitConfig.Create"/>: a validated config or the list of
/// reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record SubmitConfigResult
{
    private protected SubmitConfigResult() { }
}

internal sealed record SubmitConfigValid(SubmitConfig Config) : SubmitConfigResult;

internal sealed record SubmitConfigInvalid(IReadOnlyList<string> Errors) : SubmitConfigResult;
