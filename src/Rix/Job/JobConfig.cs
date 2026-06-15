using System.Diagnostics.CodeAnalysis;
using Rix.Agents;

namespace Rix.Job;

internal record JobConfig
{
    internal RepoIdentifier Repo { get; }
    internal string Prompt { get; }
    internal ReadToken ReadToken { get; }
    internal MaxTokens MaxTokens { get; }
    internal TimeoutMinutes TimeoutMinutes { get; }
    internal DirectoryPath WorkDir { get; }
    internal DirectoryPath OutputDir { get; }
    internal AgentKind Agent { get; }

    internal const int DefaultMaxTokens = 50_000;
    internal const int DefaultTimeoutMinutes = 30;

    /// <summary>Private so a <see cref="JobConfig"/> can only be produced by <see cref="Create"/>,
    /// which guarantees every field is validated — the type can never exist in an invalid state.</summary>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters",
        Justification = "All 8 fields are required by the record; they are set once here and are immutable after construction.")]
    private JobConfig(
        RepoIdentifier repo,
        string prompt,
        ReadToken readToken,
        MaxTokens maxTokens,
        TimeoutMinutes timeoutMinutes,
        DirectoryPath workDir,
        DirectoryPath outputDir,
        AgentKind agent)
    {
        Repo = repo;
        Prompt = prompt;
        ReadToken = readToken;
        MaxTokens = maxTokens;
        TimeoutMinutes = timeoutMinutes;
        WorkDir = workDir;
        OutputDir = outputDir;
        Agent = agent;
    }

    /// <summary>Validates and transforms raw CLI/environment inputs into a strongly-typed
    /// <see cref="JobConfig"/>. Every field is checked and parsed up front and all errors are
    /// collected, so a <see cref="JobConfigValid"/> is produced only when the whole configuration is
    /// well-formed — business logic downstream never sees an invalid value.</summary>
    internal static JobConfigResult Create(
        string repo,
        string prompt,
        string readToken,
        JobInputOptions options)
    {
        var errors = new List<string>();

        // Unwraps a parse result, recording its message (prefixed with the flag) on failure so all
        // validation errors accumulate in one pass. Centralises the success/error/fail-safe handling.
        T? Collect<T>(ParseResult<T> result, string flag) where T : class => result switch
        {
            ParseSuccess<T> ok => ok.Value,
            ParseError<T> bad => Fail<T>($"{flag}: {bad.Error}"),
            var other => Fail<T>($"{flag}: could not be parsed ({other.GetType().Name})"),
        };

        T? Fail<T>(string error) where T : class
        {
            errors.Add(error);
            return null;
        }

        RepoIdentifier? parsedRepo = null;
        if (string.IsNullOrWhiteSpace(repo))
            errors.Add("--repo is required");
        else
            parsedRepo = Collect(RepoIdentifier.Parse(repo), "--repo");

        if (string.IsNullOrWhiteSpace(prompt))
            errors.Add("--prompt is required");

        if (string.IsNullOrWhiteSpace(readToken))
            errors.Add("--read-token is required");

        var resolvedMaxTokens = options.MaxTokens ?? DefaultMaxTokens;
        if (resolvedMaxTokens <= 0)
            errors.Add("--max-tokens must be a positive integer");

        var resolvedTimeout = options.TimeoutMinutes ?? DefaultTimeoutMinutes;
        if (resolvedTimeout <= 0)
            errors.Add("--timeout must be a positive integer");

        var resolvedWorkDir = string.IsNullOrWhiteSpace(options.WorkDir) ? Path.GetTempPath() : options.WorkDir;
        var parsedWorkDir = Collect(DirectoryPath.Parse(resolvedWorkDir), "--work-dir");

        DirectoryPath? parsedOutputDir = null;
        if (string.IsNullOrWhiteSpace(options.OutputDir))
            errors.Add("--output-dir is required");
        else
            parsedOutputDir = Collect(DirectoryPath.Parse(options.OutputDir), "--output-dir");

        if (!AgentKindParser.TryParse(options.Agent, out var resolvedAgent, out var agentError))
            errors.Add($"--agent: {agentError}");

        if (errors.Count > 0)
            return new JobConfigInvalid([.. errors]);

        // Non-null here: any blank or unparseable input would have added an error above.
        return new JobConfigValid(new JobConfig(
            repo: parsedRepo!,
            prompt: prompt,
            readToken: new ReadToken(readToken),
            maxTokens: new MaxTokens(resolvedMaxTokens),
            timeoutMinutes: new TimeoutMinutes(resolvedTimeout),
            workDir: parsedWorkDir!,
            outputDir: parsedOutputDir!,
            agent: resolvedAgent));
    }
}

/// <summary>Groups the optional CLI/environment inputs to <see cref="JobConfig.Create"/>, keeping its
/// signature small and letting callers set only the values they care about.</summary>
internal record JobInputOptions(
    int? MaxTokens = null,
    int? TimeoutMinutes = null,
    string? WorkDir = null,
    string? OutputDir = null,
    string? Agent = null
);

/// <summary>The result of <see cref="JobConfig.Create"/>: a validated config or the list of
/// reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record JobConfigResult
{
    private protected JobConfigResult() { }
}

internal sealed record JobConfigValid(JobConfig Config) : JobConfigResult;

internal sealed record JobConfigInvalid(IReadOnlyList<string> Errors) : JobConfigResult;
