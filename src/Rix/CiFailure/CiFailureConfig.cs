using Rix.Job;

namespace Rix.CiFailure;

/// <summary>The validated configuration behind <c>rix ci-failure</c>, which checks whether a
/// workflow run failed and, only if it did, runs a coding agent against the failure. Carries the
/// facts needed to perform the check itself (<see cref="Repo"/>, <see cref="ReadToken"/>,
/// <see cref="RunId"/>) plus the still-raw job inputs, because the agent's prompt and its
/// <c>/push</c> allow-list aren't known until a failure is actually detected —
/// <see cref="ToJobConfig"/> builds the job's half on demand once they are.</summary>
internal sealed record CiFailureConfig
{
    internal RepoIdentifier Repo { get; }
    internal GitReadToken ReadToken { get; }
    internal RunId RunId { get; }

    /// <summary>Kept raw rather than pre-built into a <see cref="JobConfig"/>: a <see cref="JobConfig"/>
    /// cannot exist without a prompt, and there is no prompt until the run is known to have failed.</summary>
    private JobInputs JobInputs { get; }

    /// <summary>Private so a <see cref="CiFailureConfig"/> can only be produced by
    /// <see cref="Create"/>, which guarantees every field is validated — the type can never exist
    /// in an invalid state.</summary>
    private CiFailureConfig(RepoIdentifier repo, GitReadToken readToken, RunId runId, JobInputs jobInputs)
    {
        Repo = repo;
        ReadToken = readToken;
        RunId = runId;
        JobInputs = jobInputs;
    }

    /// <summary>Validates raw CLI/environment inputs in one pass: the run-identifying fields it
    /// parses itself, and the job's fields via <see cref="JobConfig.Validate"/> — which reports
    /// whether a <see cref="JobConfig"/> could be built without building one, so no prompt has to be
    /// invented here. <c>--repo</c> and <c>--read-token</c> are checked by both sides, so identical
    /// complaints are collapsed before being returned.</summary>
    internal static CiFailureConfigResult Create(CiFailureInputs inputs)
    {
        var errors = new List<string>();

        RepoIdentifier? repo = null;
        if (string.IsNullOrWhiteSpace(inputs.Job.Repo))
            errors.Add("--repo is required");
        else
            repo = RepoIdentifier.Parse(inputs.Job.Repo).Collect(errors, "--repo");

        if (string.IsNullOrWhiteSpace(inputs.Job.ReadToken))
            errors.Add("--read-token is required");

        var runId = NumericFlag.ParsePositiveInt<long>(inputs.RunId, null, "--run-id", errors);

        errors.AddRange(JobConfig.Validate(inputs.Job));

        if (errors.Count > 0)
            return new CiFailureConfigInvalid([.. errors.Distinct()]);

        // Non-null here: any blank or unparseable input would have added an error above.
        var config = new CiFailureConfig(repo!, new GitReadToken(inputs.Job.ReadToken), new RunId(runId), inputs.Job);
        return new CiFailureConfigValid(config);
    }

    /// <summary>Builds the agent-running half of this config, now that a failure has actually been
    /// detected and both missing pieces are known: the <paramref name="prompt"/> describing the
    /// failure, and <paramref name="allowedPushBranch"/>, the failing run's own branch — the only
    /// branch a fix for it could sensibly be pushed to, which is why it's derived here rather than
    /// accepted as a caller-supplied input.</summary>
    internal JobConfig ToJobConfig(string prompt, BranchName allowedPushBranch)
    => JobConfig.Create(JobInputs with { AllowedPushBranches = allowedPushBranch.Value }, prompt) switch
    {
        JobConfigValid v => v.Config,
        // Unreachable: Create already proved these inputs parse, and neither substituted value can
        // fail validation - the prompt is non-blank by construction and any branch name is allowed.
        var result => throw new InvalidOperationException($"CiFailureConfig.Create already validated these inputs: {result}"),
    };
}

/// <summary>The raw, unvalidated CLI/environment inputs to <see cref="CiFailureConfig.Create"/>:
/// <see cref="RunId"/> plus a <see cref="JobInputs"/> carrying everything <see cref="JobConfig"/>
/// needs (including the shared <c>Repo</c>/<c>ReadToken</c>). Wrapping <see cref="JobInputs"/>
/// directly, rather than re-listing its fields, means a new <c>job</c> option needs no matching
/// field here to stay in sync. <see cref="JobInputs.AllowedPushBranches"/> is ignored —
/// <see cref="CiFailureConfig.ToJobConfig"/> always derives it from the failing run.</summary>
internal sealed record CiFailureInputs(string RunId, JobInputs Job);

/// <summary>The result of <see cref="CiFailureConfig.Create"/>: a validated config or the list
/// of reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record CiFailureConfigResult
{
    private protected CiFailureConfigResult() { }
}

internal sealed record CiFailureConfigValid(CiFailureConfig Config) : CiFailureConfigResult;

internal sealed record CiFailureConfigInvalid(IReadOnlyList<string> Errors) : CiFailureConfigResult;
