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

    /// <summary>The only branches <c>/push</c> may deliver to. Empty (the default) means
    /// <c>/push</c> is disabled — an operator opts in by naming the branches this run may touch.</summary>
    internal IReadOnlyList<RixBranchName> AllowedPushBranches { get; }

    internal const int DefaultMaxTokens = 50_000;
    internal const int DefaultTimeoutMinutes = 30;
    internal const AgentKind DefaultAgent = AgentKind.OpenCode;

    /// <summary>Private so a <see cref="JobConfig"/> can only be produced by <see cref="Create"/>,
    /// which guarantees every field is validated — the type can never exist in an invalid state.</summary>
    private JobConfig
    (
        RepoIdentifier repo,
        GitReadToken readToken,
        TimeoutMinutes timeoutMinutes,
        DirectoryPath workDir,
        DirectoryPath outputDir,
        AgentConfig agent,
        IReadOnlyList<RixBranchName> allowedPushBranches
    )
    {
        Repo = repo;
        ReadToken = readToken;
        TimeoutMinutes = timeoutMinutes;
        WorkDir = workDir;
        OutputDir = outputDir;
        Agent = agent;
        AllowedPushBranches = allowedPushBranches;
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

        var resolvedMaxTokens = ParsePositiveInt(inputs.MaxTokens, DefaultMaxTokens, "--max-tokens", errors);
        var resolvedTimeout = ParsePositiveInt(inputs.TimeoutMinutes, DefaultTimeoutMinutes, "--timeout", errors);

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

        var resolvedModel = string.IsNullOrWhiteSpace(inputs.Model) ? null : inputs.Model;

        // No key is required when model is left unset - opencode then picks its own free model.
        // The env var name is only resolved (and validated) once a key actually needs exporting.
        string? resolvedApiKey = string.IsNullOrWhiteSpace(inputs.AgentApiKey) ? null : inputs.AgentApiKey;
        string? resolvedApiKeyEnv = resolvedApiKey is null
            ? null
            : AgentCredential.ResolveEnvName(resolvedAgent, inputs.AgentApiKeyEnv).Collect(errors, "--agent-api-key-env");

        var allowedPushBranches = ParseAllowedPushBranches(inputs.AllowedPushBranches, errors);

        if (errors.Count > 0)
            return new JobConfigInvalid([.. errors]);

        // Non-null here: any blank or unparseable input would have added an error above.
        var config = new JobConfig
        (
            repo: parsedRepo!,
            readToken: new GitReadToken(readToken),
            timeoutMinutes: new TimeoutMinutes(resolvedTimeout),
            workDir: parsedWorkDir!,
            outputDir: parsedOutputDir!,
            agent: new AgentConfig(resolvedAgent, prompt, new MaxTokens(resolvedMaxTokens), resolvedModel, resolvedApiKey, resolvedApiKeyEnv),
            allowedPushBranches: allowedPushBranches
        );
        return new JobConfigValid(config);
    }

    /// <summary>Parses the raw comma-separated <c>--allowed-push-branches</c> value into the
    /// <c>rix/*</c> branches the <c>/push</c> API endpoint may deliver to. Blank input (the flag was
    /// never set) means <c>/push</c> permits nothing, so the result is the empty list — an operator
    /// must opt in to letting the agent push at all. Each non-blank entry must be a well-formed
    /// <c>rix/*</c> branch name, and any malformed entry is collected as an error via
    /// <see cref="ParseResultExtensions.Collect{T}"/> so the caller's typo is reported instead of
    /// silently dropping the restriction. Duplicates are dropped.</summary>
    private static List<RixBranchName> ParseAllowedPushBranches(string? raw, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => RixBranchName.Parse(entry).Collect(errors, "--allowed-push-branches"))
            .OfType<RixBranchName>()
            .Distinct()
            .ToList();
    }

    /// <summary>Parses a raw <c>--max-tokens</c>/<c>--timeout</c>-style value: blank resolves to
    /// <paramref name="defaultValue"/> (the flag was never set), and anything else must parse as a
    /// positive integer or <paramref name="errors"/> gets a message naming exactly what was wrong -
    /// unparseable text or a non-positive number - rather than silently falling back to the default,
    /// which would hide a caller's typo instead of reporting it.</summary>
    private static int ParsePositiveInt(string? raw, int defaultValue, string flag, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;
        if (!int.TryParse(raw, out var value))
        {
            errors.Add($"{flag} must be an integer, got '{raw}'");
            return defaultValue;
        }
        if (value <= 0)
        {
            errors.Add($"{flag} must be a positive integer");
            return defaultValue;
        }
        return value;
    }
}

/// <summary>How the coding agent should be run: which agent (<see cref="AgentKind"/>), the task
/// <paramref name="Prompt"/> it receives, its token budget, and an optional <paramref name="Model"/>
/// identifier forwarded verbatim to the agent CLI (e.g. <c>openai/gpt-4o</c> for opencode) — rix
/// does not interpret or validate it, since which providers/models an agent CLI accepts is entirely
/// that CLI's concern. Groups the inputs the <c>--agent</c>, <c>--prompt</c>, <c>--max-tokens</c>,
/// and <c>--model</c> flags configure.
/// <paramref name="ApiKey"/> and <paramref name="ApiKeyEnv"/> (already resolved and validated by
/// <see cref="AgentCredential.ResolveEnvName"/>) are <see cref="JobRunner"/>'s instructions for
/// which single env var to add to the agent invocation's <see cref="AgentInvocation.EnvironmentOverrides"/>
/// — never null together, and never both null unless no key was supplied at all.</summary>
internal sealed record AgentConfig
(
    AgentKind Kind,
    string Prompt,
    MaxTokens MaxTokens,
    string? Model = null,
    string? ApiKey = null,
    string? ApiKeyEnv = null
);

/// <summary>The raw, unvalidated CLI/environment inputs to <see cref="JobConfig.Create"/>: required
/// values first, then the optional ones (which default to <c>null</c> so callers set only what they
/// care about). <see cref="JobConfig.Create"/> is the boundary that turns these primitives into the
/// always-valid, strongly-typed <see cref="JobConfig"/>.</summary>
internal record JobInputs
(
    string Repo,
    string Prompt,
    string ReadToken,
    string? MaxTokens = null,
    string? TimeoutMinutes = null,
    string? WorkDir = null,
    string? OutputDir = null,
    string? Agent = null,
    string? Model = null,
    string? AgentApiKey = null,
    string? AgentApiKeyEnv = null,
    string? AllowedPushBranches = null
);

/// <summary>The result of <see cref="JobConfig.Create"/>: a validated config or the list of
/// reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record JobConfigResult
{
    private protected JobConfigResult() { }
}

internal sealed record JobConfigValid(JobConfig Config) : JobConfigResult;

internal sealed record JobConfigInvalid(IReadOnlyList<string> Errors) : JobConfigResult;
