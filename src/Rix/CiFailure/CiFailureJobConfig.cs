using Rix.Job;

namespace Rix.CiFailure;

/// <summary>The plain, already-validated inputs behind <c>rix ci-failure-job</c>, which checks a
/// run and, only if it failed, runs the agent against the prompt built from that failure. Holds
/// <see cref="Inputs"/> rather than a <see cref="CiFailure.CiFailureConfig"/>/<see cref="Job.JobConfig"/>
/// pair, since the real prompt and <c>/push</c> allow-list aren't known until a failure is
/// actually detected — <see cref="ToJobConfig"/> builds the job's half on demand once they are,
/// instead of forcing <see cref="Job.JobConfig"/> to expose a way to patch an existing instance.</summary>
internal sealed record CiFailureJobConfig
{
    internal CiFailureJobInputs Inputs { get; }

    /// <summary>Never seen by anything real: <see cref="ToJobConfig"/> only falls back to this when
    /// called before a failure is detected (e.g. by callers that just need a field <c>Job</c>'s
    /// shape carries, such as <see cref="Job.JobConfig.OutputDir"/>).</summary>
    private const string PlaceholderPrompt = "(pending ci-failure detection)";

    /// <summary>Private so a <see cref="CiFailureJobConfig"/> can only be produced by
    /// <see cref="Create"/>, which guarantees <see cref="Inputs"/> is validated — the type can
    /// never exist in an invalid state.</summary>
    private CiFailureJobConfig(CiFailureJobInputs inputs) => Inputs = inputs;

    /// <summary>Validates raw CLI/environment inputs by delegating to <see cref="CiFailureConfig.Create"/>
    /// and <see cref="JobConfig.Create"/>, collecting every error from both in one pass so a
    /// <see cref="CiFailureJobConfigValid"/> is produced only when the whole configuration is
    /// well-formed. Neither validated config is kept: <see cref="Inputs"/> alone drives
    /// <see cref="ToCiFailureConfig"/> and <see cref="ToJobConfig"/>.</summary>
    internal static CiFailureJobConfigResult Create(CiFailureJobInputs inputs)
    {
        var errors = new List<string>();

        switch (CiFailureConfig.Create(inputs.Job.Repo, inputs.Job.ReadToken, inputs.RunId))
        {
            case CiFailureConfigInvalid i: errors.AddRange(i.Errors); break;
        }

        // AllowedPushBranches is never taken from inputs: it's derived by CiFailureJobRunner from
        // the failing run's own branch once a failure is actually detected, not accepted as a
        // caller-supplied parameter. Prompt is likewise always overwritten here, regardless of
        // what inputs.Job carried - the real prompt is only known once CiFailureRunner detects a
        // failure.
        switch (JobConfig.Create(inputs.Job with { Prompt = PlaceholderPrompt }))
        {
            case JobConfigInvalid i: errors.AddRange(i.Errors); break;
        }

        if (errors.Count > 0)
            return new CiFailureJobConfigInvalid([.. errors.Distinct()]);

        return new CiFailureJobConfigValid(new CiFailureJobConfig(inputs));
    }

    /// <summary>Rebuilds the <see cref="CiFailure.CiFailureConfig"/> half of <see cref="Inputs"/> —
    /// cheap re-parsing rather than keeping the object around, since <see cref="Create"/> already
    /// proved it parses cleanly.</summary>
    internal CiFailureConfig ToCiFailureConfig()
    => CiFailureConfig.Create(Inputs.Job.Repo, Inputs.Job.ReadToken, Inputs.RunId) switch
    {
        CiFailureConfigValid v => v.Config,
        var result => throw new InvalidOperationException($"CiFailureJobConfig.Create already validated these inputs: {result}"),
    };

    /// <summary>Rebuilds the <see cref="Job.JobConfig"/> half of <see cref="Inputs"/>, substituting
    /// the real <paramref name="prompt"/> and <paramref name="allowedPushBranch"/> now that a CI
    /// failure has actually been detected (both default to "not known yet", for callers that only
    /// need a field the placeholder-prompted shape already carries, e.g. <see cref="Job.JobConfig.OutputDir"/>).</summary>
    internal JobConfig ToJobConfig(string? prompt = null, string? allowedPushBranch = null)
    => JobConfig.Create(Inputs.Job with { Prompt = prompt ?? PlaceholderPrompt, AllowedPushBranches = allowedPushBranch }) switch
    {
        JobConfigValid v => v.Config,
        var result => throw new InvalidOperationException($"CiFailureJobConfig.Create already validated these inputs: {result}"),
    };
}

/// <summary>The raw, unvalidated CLI/environment inputs to <see cref="CiFailureJobConfig.Create"/>:
/// <see cref="RunId"/> plus a <see cref="JobInputs"/> carrying everything <see cref="JobConfig.Create"/>
/// needs (including the shared <c>Repo</c>/<c>ReadToken</c>). Wrapping <see cref="JobInputs"/>
/// directly, rather than re-listing its fields, means a new <c>job</c> option needs no matching
/// field here to stay in sync. <see cref="JobInputs.Prompt"/> and <see cref="JobInputs.AllowedPushBranches"/>
/// are ignored — <see cref="CiFailureJobConfig.ToJobConfig"/> always overwrites both, since the
/// real values are only known once a failure is actually detected.</summary>
internal sealed record CiFailureJobInputs(string RunId, JobInputs Job);

/// <summary>The result of <see cref="CiFailureJobConfig.Create"/>: a validated config or the list
/// of reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record CiFailureJobConfigResult
{
    private protected CiFailureJobConfigResult() { }
}

internal sealed record CiFailureJobConfigValid(CiFailureJobConfig Config) : CiFailureJobConfigResult;

internal sealed record CiFailureJobConfigInvalid(IReadOnlyList<string> Errors) : CiFailureJobConfigResult;
