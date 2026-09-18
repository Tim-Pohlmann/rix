using Rix.Job;

namespace Rix.CiFailure;

/// <summary>The validated configuration behind <c>rix ci-failure</c>, which checks whether a
/// workflow run failed and, only if it did, runs a coding agent against the failure. Carries the
/// <see cref="RunId"/> this command adds plus the still-raw job inputs, because the agent's prompt
/// and its <c>/push</c> allow-list aren't known until a failure is actually detected —
/// <see cref="ToJobConfig"/> builds the job's half on demand once they are.</summary>
internal sealed record CiFailureConfig
{
    internal RunId RunId { get; }

    /// <summary>Kept raw rather than pre-built into a <see cref="JobConfig"/>: a <see cref="JobConfig"/>
    /// cannot exist without a prompt, and there is no prompt until the run is known to have failed.</summary>
    private JobInputs JobInputs { get; }

    /// <summary>The <c>--repo</c> job input, in the form the failure check needs it — that check
    /// runs before any <see cref="JobConfig"/> exists to read it from. Converted once here rather
    /// than per read, and derived from <see cref="JobInputs"/> rather than supplied alongside it,
    /// so the two can't describe different repos.</summary>
    internal RepoIdentifier Repo { get; }

    /// <summary>The <c>--read-token</c> job input, wrapped once for the same reason as
    /// <see cref="Repo"/>.</summary>
    internal GitReadToken ReadToken { get; }

    /// <summary>Private so a <see cref="CiFailureConfig"/> can only be produced by
    /// <see cref="Create"/>, which guarantees every field is validated — the type can never exist
    /// in an invalid state.</summary>
    private CiFailureConfig(RunId runId, JobInputs jobInputs)
    {
        RunId = runId;
        JobInputs = jobInputs;
        Repo = RepoIdentifier.Parse(jobInputs.Repo) switch
        {
            ParseSuccess<RepoIdentifier> parsed => parsed.Value,
            // Unreachable: Create only gets here once JobConfig.Validate, which parses --repo the
            // same way, reported no error.
            var result => throw new InvalidOperationException($"JobConfig.Validate accepted a repo that does not parse: {result}"),
        };
        ReadToken = new GitReadToken(jobInputs.ReadToken);
    }

    /// <summary>Validates raw CLI/environment inputs in one pass. <c>--run-id</c> is the only one
    /// this command adds, so it is the only one checked here; everything else is a job input, left
    /// to <see cref="JobConfig.Validate"/> — which reports whether a <see cref="JobConfig"/> could be
    /// built without building one, so no prompt has to be invented here. Validating a job input a
    /// second time just because this command also needs it would report every complaint about it
    /// twice; the two it needs early are converted by the constructor instead.</summary>
    internal static CiFailureConfigResult Create(CiFailureInputs inputs)
    {
        var errors = new List<string>(JobConfig.Validate(inputs.Job));
        var runId = NumericFlag.ParsePositiveInt<long>(inputs.RunId, null, "--run-id", errors);

        if (errors.Count > 0)
            return new CiFailureConfigInvalid([.. errors]);

        return new CiFailureConfigValid(new CiFailureConfig(new RunId(runId), inputs.Job));
    }

    /// <summary>Builds the agent-running half of this config, now that a failure has actually been
    /// detected and both missing pieces are known: the <paramref name="prompt"/> describing the
    /// failure, and <paramref name="allowedPushBranch"/>, the failing run's own branch — the only
    /// branch a fix for it could sensibly be pushed to, which is why it's derived here rather than
    /// accepted as a caller-supplied input. The branch is handed over as a one-element allow-list
    /// rather than written into <see cref="JobInputs.AllowedPushBranches"/>, whose comma-separated
    /// form would split a branch name containing a comma into two entries — permitting pushes to
    /// branches that merely share those halves while rejecting the failing branch itself.</summary>
    internal JobConfig ToJobConfig(string prompt, BranchName allowedPushBranch)
    => JobConfig.Create(JobInputs with { Prompt = prompt }, [allowedPushBranch]) switch
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
/// field here to stay in sync. <see cref="JobInputs.Prompt"/> and
/// <see cref="JobInputs.AllowedPushBranches"/> are ignored if set: both describe a failure that
/// hasn't happened yet, so <see cref="CiFailureConfig.ToJobConfig"/> always derives them from the
/// detected run.</summary>
internal sealed record CiFailureInputs(string RunId, JobInputs Job);

/// <summary>The result of <see cref="CiFailureConfig.Create"/>: a validated config or the list
/// of reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record CiFailureConfigResult
{
    private protected CiFailureConfigResult() { }
}

internal sealed record CiFailureConfigValid(CiFailureConfig Config) : CiFailureConfigResult;

internal sealed record CiFailureConfigInvalid(IReadOnlyList<string> Errors) : CiFailureConfigResult;
