using Rix.Agents;

namespace Rix.Job;

internal record JobConfig
{
    internal RepoIdentifier Repo { get; init; }
    internal GitReadToken ReadToken { get; init; }
    internal TimeoutMinutes TimeoutMinutes { get; init; }
    internal DirectoryPath WorkDir { get; init; }
    internal DirectoryPath OutputDir { get; init; }
    internal AgentConfig Agent { get; init; }

    /// <summary>The only branches <c>/push</c> may deliver to. Empty (the default) means
    /// <c>/push</c> is disabled — an operator opts in by naming the branches this run may touch.
    /// Any branch name is acceptable (unlike <c>rix/*</c>-restricted branches the agent creates
    /// via <c>/pr</c>), since these already exist on the remote before the job ever runs.</summary>
    internal IReadOnlyList<BranchName> AllowedPushBranches { get; init; }

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
        IReadOnlyList<BranchName> allowedPushBranches
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
    /// well-formed — business logic downstream never sees an invalid value. <paramref name="prompt"/>
    /// is separate from <paramref name="inputs"/> because it is the one value a caller may not know
    /// at CLI-parse time: <c>rix ci-failure</c> only learns it once a failure is actually detected,
    /// and validates everything else up front via <see cref="Validate"/>.</summary>
    internal static JobConfigResult Create(JobInputs inputs, string prompt)
    {
        var errors = new List<string>();
        var parsed = Parse(inputs, errors);

        if (string.IsNullOrWhiteSpace(prompt))
            errors.Add("--prompt is required");

        if (errors.Count > 0)
            return new JobConfigInvalid([.. errors]);

        // Non-null here: Parse only returns null after adding at least one error.
        return new JobConfigValid(parsed!.ToConfig(prompt));
    }

    /// <summary>Reports whether <paramref name="inputs"/> would produce a valid <see cref="JobConfig"/>,
    /// without building one and without needing a prompt. Lets <c>rix ci-failure</c> reject bad
    /// input at CLI-parse time — before it knows the prompt, and long before it knows whether it
    /// will run the agent at all — instead of constructing a throwaway config around a placeholder.</summary>
    internal static IReadOnlyList<string> Validate(JobInputs inputs)
    {
        var errors = new List<string>();
        Parse(inputs, errors);
        return errors;
    }

    /// <summary>The shared parsing core behind <see cref="Create"/> and <see cref="Validate"/>:
    /// converts every field of <paramref name="inputs"/> to its strong type, appending a message to
    /// <paramref name="errors"/> for each one that fails. Returns <c>null</c> exactly when it added
    /// an error, so <see cref="Validate"/> can ignore the result while <see cref="Create"/> uses it.</summary>
    private static Parsed? Parse(JobInputs inputs, List<string> errors)
    {
        var (repo, readToken) = (inputs.Repo, inputs.ReadToken);

        RepoIdentifier? parsedRepo = null;
        if (string.IsNullOrWhiteSpace(repo))
            errors.Add("--repo is required");
        else
            parsedRepo = RepoIdentifier.Parse(repo).Collect(errors, "--repo");

        if (string.IsNullOrWhiteSpace(readToken))
            errors.Add("--read-token is required");

        var resolvedMaxTokens = NumericFlag.ParsePositiveInt<int>(inputs.MaxTokens, DefaultMaxTokens, "--max-tokens", errors);
        var resolvedTimeout = NumericFlag.ParsePositiveInt<int>(inputs.TimeoutMinutes, DefaultTimeoutMinutes, "--timeout", errors);

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

        var allowedPushBranches = ParseAllowedPushBranches(inputs.AllowedPushBranches);

        if (errors.Count > 0)
            return null;

        // Non-null here: any blank or unparseable input would have added an error above.
        return new Parsed
        (
            Repo: parsedRepo!,
            ReadToken: new GitReadToken(readToken),
            TimeoutMinutes: new TimeoutMinutes(resolvedTimeout),
            WorkDir: parsedWorkDir!,
            OutputDir: parsedOutputDir!,
            Agent: resolvedAgent,
            MaxTokens: new MaxTokens(resolvedMaxTokens),
            Model: resolvedModel,
            ApiKey: resolvedApiKey,
            ApiKeyEnv: resolvedApiKeyEnv,
            AllowedPushBranches: allowedPushBranches
        );
    }

    /// <summary>A <see cref="JobConfig"/> minus its prompt: everything <see cref="Parse"/> could
    /// determine from <see cref="JobInputs"/> alone. Exists so <see cref="Validate"/> and
    /// <see cref="Create"/> share one parsing pass without <see cref="Validate"/> having to invent a
    /// prompt just to reach the end of it.</summary>
    private sealed record Parsed
    (
        RepoIdentifier Repo,
        GitReadToken ReadToken,
        TimeoutMinutes TimeoutMinutes,
        DirectoryPath WorkDir,
        DirectoryPath OutputDir,
        AgentKind Agent,
        MaxTokens MaxTokens,
        string? Model,
        string? ApiKey,
        string? ApiKeyEnv,
        IReadOnlyList<BranchName> AllowedPushBranches
    )
    {
        internal JobConfig ToConfig(string prompt) => new
        (
            repo: Repo,
            readToken: ReadToken,
            timeoutMinutes: TimeoutMinutes,
            workDir: WorkDir,
            outputDir: OutputDir,
            agent: new AgentConfig(Agent, prompt, MaxTokens, Model, ApiKey, ApiKeyEnv),
            allowedPushBranches: AllowedPushBranches
        );
    }

    /// <summary>Parses the raw comma-separated <c>--allowed-push-branches</c> value into the
    /// branches the <c>/push</c> API endpoint may deliver to. Blank input (the flag was
    /// never set) means <c>/push</c> permits nothing, so the result is the empty list — an operator
    /// must opt in to letting the agent push at all. Unlike the <c>rix/*</c>-restricted branches the
    /// agent creates via <c>/pr</c>, any branch name is acceptable here, since these already exist on
    /// the remote before the job ever runs. Duplicates are dropped.</summary>
    private static List<BranchName> ParseAllowedPushBranches(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => new BranchName(entry))
            .Distinct()
            .ToList();
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
/// always-valid, strongly-typed <see cref="JobConfig"/>. The prompt is deliberately absent — it is
/// passed to <see cref="JobConfig.Create"/> separately, since <c>rix ci-failure</c> validates these
/// inputs long before it knows what the prompt will be.</summary>
internal record JobInputs
(
    string Repo,
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
