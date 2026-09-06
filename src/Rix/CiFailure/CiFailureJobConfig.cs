using Rix.Job;

namespace Rix.CiFailure;

/// <summary>Combines a <see cref="CiFailure.CiFailureConfig"/> (how to check a run) with a
/// <see cref="Job.JobConfig"/> (how to run the agent) for <c>rix ci-failure-job</c>, which checks
/// a run and, only if it failed, runs the agent against the prompt built from that failure. The
/// job config's prompt is a placeholder until <see cref="JobConfig.WithPrompt"/> substitutes the
/// real one once a failure is actually detected.</summary>
internal sealed record CiFailureJobConfig
{
    internal CiFailureConfig CiFailure { get; }
    internal JobConfig Job { get; }

    /// <summary>Never seen by anything real: <see cref="Job"/> is only used via
    /// <see cref="JobConfig.WithPrompt"/> once a failure is detected, which always replaces it
    /// first.</summary>
    private const string PlaceholderPrompt = "(pending ci-failure detection)";

    /// <summary>Private so a <see cref="CiFailureJobConfig"/> can only be produced by
    /// <see cref="Create"/>, which guarantees every field is validated — the type can never exist
    /// in an invalid state.</summary>
    private CiFailureJobConfig(CiFailureConfig ciFailure, JobConfig job)
    {
        CiFailure = ciFailure;
        Job = job;
    }

    /// <summary>Validates and transforms raw CLI/environment inputs into a strongly-typed
    /// <see cref="CiFailureJobConfig"/> by delegating to <see cref="CiFailureConfig.Create"/> and
    /// <see cref="JobConfig.Create"/>, collecting every error from both in one pass so a
    /// <see cref="CiFailureJobConfigValid"/> is produced only when the whole configuration is
    /// well-formed.</summary>
    internal static CiFailureJobConfigResult Create(CiFailureJobInputs inputs)
    {
        var errors = new List<string>();

        CiFailureConfig? ciFailure = null;
        switch (CiFailureConfig.Create(inputs.Repo, inputs.ReadToken, inputs.RunId))
        {
            case CiFailureConfigValid v: ciFailure = v.Config; break;
            case CiFailureConfigInvalid i: errors.AddRange(i.Errors); break;
        }

        JobConfig? job = null;
        // AllowedPushBranches is never taken from inputs: it's derived by CiFailureJobRunner from
        // the failing run's own branch once a failure is actually detected, not accepted as a
        // caller-supplied parameter (see JobConfig.WithAllowedPushBranches).
        var jobInputs = new JobInputs
        (
            Repo: inputs.Repo,
            Prompt: PlaceholderPrompt,
            ReadToken: inputs.ReadToken,
            MaxTokens: inputs.MaxTokens,
            TimeoutMinutes: inputs.TimeoutMinutes,
            WorkDir: inputs.WorkDir,
            OutputDir: inputs.OutputDir,
            Agent: inputs.Agent,
            Model: inputs.Model
        );
        switch (JobConfig.Create(jobInputs))
        {
            case JobConfigValid v: job = v.Config; break;
            case JobConfigInvalid i: errors.AddRange(i.Errors); break;
        }

        if (errors.Count > 0)
            return new CiFailureJobConfigInvalid([.. errors.Distinct()]);

        // Non-null here: any validation failure above would have added an error and returned.
        return new CiFailureJobConfigValid(new CiFailureJobConfig(ciFailure!, job!));
    }
}

/// <summary>The raw, unvalidated CLI/environment inputs to <see cref="CiFailureJobConfig.Create"/>:
/// the union of <see cref="CiFailureConfig"/>'s and <see cref="JobConfig"/>'s inputs, minus the
/// prompt (derived from the detected failure, never supplied directly) and the duplicate
/// repo/read-token pair (shared by both halves).</summary>
internal sealed record CiFailureJobInputs
(
    string Repo,
    string ReadToken,
    string RunId,
    string? MaxTokens = null,
    string? TimeoutMinutes = null,
    string? WorkDir = null,
    string? OutputDir = null,
    string? Agent = null,
    string? Model = null
);

/// <summary>The result of <see cref="CiFailureJobConfig.Create"/>: a validated config or the list
/// of reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record CiFailureJobConfigResult
{
    private protected CiFailureJobConfigResult() { }
}

internal sealed record CiFailureJobConfigValid(CiFailureJobConfig Config) : CiFailureJobConfigResult;

internal sealed record CiFailureJobConfigInvalid(IReadOnlyList<string> Errors) : CiFailureJobConfigResult;
