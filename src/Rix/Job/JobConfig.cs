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
    /// <c>/push</c> is disabled — an operator opts in by naming the branches this run may touch.
    /// Any branch name is acceptable (unlike <c>rix/*</c>-restricted branches the agent creates
    /// via <c>/pr</c>), since these already exist on the remote before the job ever runs.</summary>
    internal IReadOnlyList<BranchName> AllowedPushBranches { get; }

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
    /// well-formed — business logic downstream never sees an invalid value.
    /// <paramref name="allowedPushBranches"/> lets a caller that already holds the allow-list as
    /// values supply it directly, replacing <see cref="JobInputs.AllowedPushBranches"/> rather than
    /// being merged with it. Only the CLI has a comma-separated string to begin with; a caller that
    /// derives the allow-list (<c>ci-failure</c>, from the failing run's branch) must not flatten it
    /// back into one, since a branch name may itself contain a comma and would then be split into
    /// two entries that permit pushing to other branches while rejecting the intended one.</summary>
    internal static JobConfigResult Create(JobInputs inputs, IReadOnlyList<BranchName>? allowedPushBranches = null)
    {
        var errors = new List<string>();
        var parsed = Parse(inputs, errors, allowedPushBranches);

        if (string.IsNullOrWhiteSpace(inputs.Prompt))
            errors.Add("--prompt is required");

        if (errors.Count > 0)
            return new JobConfigInvalid([.. errors]);

        // Both non-null here: Parse only returns null after adding at least one error, and a blank
        // prompt would have added one too.
        return new JobConfigValid(parsed!.ToConfig(inputs.Prompt!));
    }

    /// <summary>Reports whether <paramref name="inputs"/> would produce a valid <see cref="JobConfig"/>,
    /// without building one — and without requiring <see cref="JobInputs.Prompt"/>, the one field a
    /// caller may legitimately not have yet. Lets <c>rix ci-failure</c> reject bad input at
    /// CLI-parse time, before it knows the prompt and long before it knows whether it will run the
    /// agent at all, instead of constructing a throwaway config around a placeholder.</summary>
    internal static IReadOnlyList<string> Validate(JobInputs inputs)
    {
        var errors = new List<string>();
        Parse(inputs, errors);
        return errors;
    }

    /// <summary>The shared parsing core behind <see cref="Create"/> and <see cref="Validate"/>:
    /// converts every field of <paramref name="inputs"/> to its strong type, appending a message to
    /// <paramref name="errors"/> for each one that fails. Returns <c>null</c> exactly when it added
    /// an error, so <see cref="Validate"/> can ignore the result while <see cref="Create"/> uses it.
    /// <paramref name="allowedPushBranches"/>, when given, is used verbatim in place of parsing
    /// <see cref="JobInputs.AllowedPushBranches"/>.</summary>
    private static Parsed? Parse(JobInputs inputs, List<string> errors, IReadOnlyList<BranchName>? allowedPushBranches = null)
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

        var resolvedPushBranches = allowedPushBranches ?? BranchName.ParseAllowList(inputs.AllowedPushBranches);

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
            AllowedPushBranches: resolvedPushBranches
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
/// — both null when no key was supplied, otherwise both set; never one without the other.</summary>
internal sealed record AgentConfig
(
    AgentKind Kind,
    string Prompt,
    MaxTokens MaxTokens,
    string? Model = null,
    string? ApiKey = null,
    string? ApiKeyEnv = null
);

/// <summary>The raw, unvalidated CLI/environment inputs to <see cref="JobConfig.Create"/>: whatever
/// the caller supplied, if anything. Which of these a job actually requires is
/// <see cref="JobConfig.Create"/>'s call, not the type's — <see cref="Prompt"/> defaults to
/// <c>null</c> like every other unsupplied flag even though <c>job</c> demands one, because
/// <c>rix ci-failure</c> builds these inputs before it knows what the prompt will be and fills it in
/// via <c>with</c> once a failure hands it one. <see cref="JobConfig.Create"/> is the boundary that
/// turns these primitives into the always-valid, strongly-typed <see cref="JobConfig"/>.</summary>
internal record JobInputs
(
    string Repo,
    string ReadToken,
    string? Prompt = null,
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
