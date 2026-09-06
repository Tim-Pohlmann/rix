namespace Rix.CiFailure;

internal sealed record CiFailureConfig
{
    internal RepoIdentifier Repo { get; }
    internal GitReadToken ReadToken { get; }
    internal long RunId { get; }

    /// <summary>Private so a <see cref="CiFailureConfig"/> can only be produced by
    /// <see cref="Create"/>, which guarantees every field is validated — the type can never exist
    /// in an invalid state.</summary>
    private CiFailureConfig(RepoIdentifier repo, GitReadToken readToken, long runId)
    {
        Repo = repo;
        ReadToken = readToken;
        RunId = runId;
    }

    /// <summary>Validates and transforms raw CLI/environment inputs into a strongly-typed
    /// <see cref="CiFailureConfig"/>, collecting every error in one pass so a
    /// <see cref="CiFailureConfigValid"/> is produced only when the whole configuration is
    /// well-formed.</summary>
    internal static CiFailureConfigResult Create(string repo, string readToken, string runId)
    {
        var errors = new List<string>();

        RepoIdentifier? parsedRepo = null;
        if (string.IsNullOrWhiteSpace(repo))
            errors.Add("--repo is required");
        else
            parsedRepo = RepoIdentifier.Parse(repo).Collect(errors, "--repo");

        if (string.IsNullOrWhiteSpace(readToken))
            errors.Add("--read-token is required");

        var parsedRunId = ParseRunId(runId, errors);

        if (errors.Count > 0)
            return new CiFailureConfigInvalid([.. errors]);

        // Non-null here: any blank or unparseable input would have added an error above.
        var config = new CiFailureConfig(parsedRepo!, new GitReadToken(readToken), parsedRunId);
        return new CiFailureConfigValid(config);
    }

    private static long ParseRunId(string? raw, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            errors.Add("--run-id is required");
            return 0;
        }
        if (!long.TryParse(raw, out var value) || value <= 0)
        {
            errors.Add($"--run-id must be a positive integer, got '{raw}'");
            return 0;
        }
        return value;
    }
}

/// <summary>The result of <see cref="CiFailureConfig.Create"/>: a validated config or the list of
/// reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record CiFailureConfigResult
{
    private protected CiFailureConfigResult() { }
}

internal sealed record CiFailureConfigValid(CiFailureConfig Config) : CiFailureConfigResult;

internal sealed record CiFailureConfigInvalid(IReadOnlyList<string> Errors) : CiFailureConfigResult;
