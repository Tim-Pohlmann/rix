namespace Rix.Job;

internal record JobConfig(
    RepoIdentifier Repo,
    string Prompt,
    ReadToken ReadToken,
    MaxTokens MaxTokens,
    TimeoutMinutes TimeoutMinutes,
    string WorkDir,
    string OutputDir
)
{
    internal const int DefaultMaxTokens = 50_000;
    internal const int DefaultTimeoutMinutes = 30;

    /// <summary>Validates and transforms raw CLI/environment inputs into a strongly-typed
    /// <see cref="JobConfig"/>. Every field is checked and parsed up front and all errors are
    /// collected, so a <see cref="JobConfigValid"/> is produced only when the whole configuration is
    /// well-formed — business logic downstream never sees an invalid value.</summary>
    internal static JobConfigResult Create(
        string repo,
        string prompt,
        string readToken,
        int? maxTokens,
        int? timeoutMinutes,
        string? workDir,
        string? outputDir)
    {
        var errors = new List<string>();

        RepoIdentifier? parsedRepo = null;
        if (string.IsNullOrWhiteSpace(repo))
            errors.Add("--repo is required");
        else switch (RepoIdentifier.Parse(repo))
        {
            case Parsed<RepoIdentifier> ok: parsedRepo = ok.Value; break;
            case ParseError<RepoIdentifier> bad: errors.Add(bad.Error); break;
        }

        if (string.IsNullOrWhiteSpace(prompt))
            errors.Add("--prompt is required");

        if (string.IsNullOrWhiteSpace(readToken))
            errors.Add("--read-token is required");

        var resolvedMaxTokens = maxTokens ?? DefaultMaxTokens;
        if (resolvedMaxTokens <= 0)
            errors.Add("--max-tokens must be a positive integer");

        var resolvedTimeout = timeoutMinutes ?? DefaultTimeoutMinutes;
        if (resolvedTimeout <= 0)
            errors.Add("--timeout must be a positive integer");

        var resolvedWorkDir = string.IsNullOrWhiteSpace(workDir) ? Path.GetTempPath() : workDir;
        if (!Directory.Exists(resolvedWorkDir))
            errors.Add($"--work-dir does not exist: {resolvedWorkDir}");

        var resolvedOutputDir = outputDir ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedOutputDir))
            errors.Add("--output-dir is required");
        else if (!Directory.Exists(resolvedOutputDir))
            errors.Add($"--output-dir does not exist: {resolvedOutputDir}");

        if (errors.Count > 0)
            return new JobConfigInvalid(errors);

        // parsedRepo is non-null here: a blank or malformed repo would have added an error above.
        return new JobConfigValid(new JobConfig(
            Repo: parsedRepo!.Value,
            Prompt: prompt,
            ReadToken: new ReadToken(readToken),
            MaxTokens: new MaxTokens(resolvedMaxTokens),
            TimeoutMinutes: new TimeoutMinutes(resolvedTimeout),
            WorkDir: resolvedWorkDir,
            OutputDir: resolvedOutputDir));
    }
}

/// <summary>The result of <see cref="JobConfig.Create"/>: a validated config or the list of
/// reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record JobConfigResult;

internal sealed record JobConfigValid(JobConfig Config) : JobConfigResult;

internal sealed record JobConfigInvalid(IReadOnlyList<string> Errors) : JobConfigResult;
