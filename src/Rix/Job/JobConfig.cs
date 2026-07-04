using Rix.Agents;

namespace Rix.Job;

internal record JobConfig
{
    internal RepoIdentifier Repo { get; }
    internal GitReadToken ReadToken { get; }
    internal TimeoutMinutes TimeoutMinutes { get; }
    internal DirectoryPath WorkDir { get; }
    internal DirectoryPath OutputDir { get; }
    internal AgentConfig Agent { get; }

    internal const int DefaultMaxTokens = 50_000;
    internal const int DefaultTimeoutMinutes = 30;
    internal const AgentKind DefaultAgent = AgentKind.Claude;

    /// <summary>Private so a <see cref="JobConfig"/> can only be produced by <see cref="Create"/>,
    /// which guarantees every field is validated — the type can never exist in an invalid state.</summary>
    private JobConfig
    (
        RepoIdentifier repo,
        GitReadToken readToken,
        TimeoutMinutes timeoutMinutes,
        DirectoryPath workDir,
        DirectoryPath outputDir,
        AgentConfig agent
    )
    {
        Repo = repo;
        ReadToken = readToken;
        TimeoutMinutes = timeoutMinutes;
        WorkDir = workDir;
        OutputDir = outputDir;
        Agent = agent;
    }

    /// <summary>Validates and transforms raw CLI/environment inputs into a strongly-typed
    /// <see cref="JobConfig"/>. Every field is checked and parsed up front and all errors are
    /// collected, so a <see cref="JobConfigValid"/> is produced only when the whole configuration is
    /// well-formed — business logic downstream never sees an invalid value.</summary>
    internal static JobConfigResult Create(JobInputs inputs)
    {
        var (repo, prompt, readToken) = (inputs.Repo, inputs.Prompt, inputs.ReadToken);
        var errors = new List<string>();

        RepoIdentifier? parsedRepo = null;
        if (string.IsNullOrWhiteSpace(repo))
            errors.Add("--repo is required");
        else
            parsedRepo = RepoIdentifier.Parse(repo).Collect(errors, "--repo");

        if (string.IsNullOrWhiteSpace(prompt))
            errors.Add("--prompt is required");

        if (string.IsNullOrWhiteSpace(readToken))
            errors.Add("--read-token is required");

        var resolvedMaxTokens = inputs.MaxTokens ?? DefaultMaxTokens;
        if (resolvedMaxTokens <= 0)
            errors.Add("--max-tokens must be a positive integer");

        var resolvedTimeout = inputs.TimeoutMinutes ?? DefaultTimeoutMinutes;
        if (resolvedTimeout <= 0)
            errors.Add("--timeout must be a positive integer");

        var resolvedWorkDir = string.IsNullOrWhiteSpace(inputs.WorkDir) switch
        {
            true => Path.GetTempPath(),
            false => inputs.WorkDir,
        };
        var parsedWorkDir = DirectoryPath.Parse(resolvedWorkDir).Collect(errors, "--work-dir");

        DirectoryPath? parsedOutputDir = null;
        if (string.IsNullOrWhiteSpace(inputs.OutputDir))
            errors.Add("--output-dir is required");
        else
            parsedOutputDir = DirectoryPath.Parse(inputs.OutputDir).Collect(errors, "--output-dir");

        var resolvedAgent = string.IsNullOrWhiteSpace(inputs.Agent) switch
        {
            true => DefaultAgent,
            false => AgentKindParser.Parse(inputs.Agent).Match
            (
                onSuccess: kind => kind,
                onError: error => { errors.Add($"--agent: {error}"); return DefaultAgent; }
            ),
        };

        if (errors.Count > 0)
            return new JobConfigInvalid([.. errors]);

        // Non-null here: any blank or unparseable input would have added an error above.
        return new JobConfigValid(new JobConfig
        (
            repo: parsedRepo!,
            readToken: new GitReadToken(readToken),
            timeoutMinutes: new TimeoutMinutes(resolvedTimeout),
            workDir: parsedWorkDir!,
            outputDir: parsedOutputDir!,
            agent: new AgentConfig(resolvedAgent, prompt, new MaxTokens(resolvedMaxTokens))
        ));
    }
}

/// <summary>How the coding agent should be run: which agent (<see cref="AgentKind"/>), the task
/// <paramref name="Prompt"/> it receives, and its token budget. Groups the inputs the <c>--agent</c>,
/// <c>--prompt</c>, and <c>--max-tokens</c> flags configure.</summary>
internal sealed record AgentConfig(AgentKind Kind, string Prompt, MaxTokens MaxTokens);

/// <summary>The raw, unvalidated CLI/environment inputs to <see cref="JobConfig.Create"/>: required
/// values first, then the optional ones (which default to <c>null</c> so callers set only what they
/// care about). <see cref="JobConfig.Create"/> is the boundary that turns these primitives into the
/// always-valid, strongly-typed <see cref="JobConfig"/>.</summary>
internal record JobInputs
(
    string Repo,
    string Prompt,
    string ReadToken,
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
