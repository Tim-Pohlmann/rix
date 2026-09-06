using Rix.Job;
using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>
/// Checks whether a workflow run failed and, only if it did, runs the coding agent against the
/// prompt built from that failure — the full pipeline behind <c>rix ci-failure-job</c>. Thin
/// orchestration over <see cref="CiFailureRunner.RunAsync"/> and <see cref="JobRunner.RunAsync"/>;
/// neither is duplicated here.
/// </summary>
internal static class CiFailureJobRunner
{
    internal static async Task<CiFailureJobOutcome> RunAsync
    (
        CiFailureJobConfig config,
        ICiFailureHost ciFailureHost,
        JobContext jobContext,
        CancellationToken cancellationToken
    )
    {
        var ciFailureResult = await CiFailureRunner.RunAsync(config.CiFailure, ciFailureHost, cancellationToken);
        if (ciFailureResult is not CiFailureDetected detected)
            return new CiFailureJobNotRun(ciFailureResult);

        // Resuming a CI failure means pushing a fix back onto the exact branch that failed - the
        // only sensible /push target here, so it's derived from the detected run rather than
        // accepted as a caller-supplied input (see JobConfig.WithAllowedPushBranches). A failing
        // branch that isn't itself a rix/* branch (e.g. CI failed on a human's PR, not a previous
        // rix run) simply gets no allow-list entry - /push stays disabled, same as an operator
        // never opting in.
        var allowedPushBranches = RixBranchName.Parse(detected.Branch).Match
        (
            onSuccess: branch => (IReadOnlyList<RixBranchName>)[branch],
            onError: _ => []
        );
        var job = config.Job.WithPrompt(detected.Prompt).WithAllowedPushBranches(allowedPushBranches);
        var jobResult = await JobRunner.RunAsync(job, jobContext, cancellationToken);
        return new CiFailureJobRan(jobResult);
    }
}

/// <summary>Whether <see cref="CiFailureJobRunner.RunAsync"/> ran the agent at all.</summary>
internal abstract record CiFailureJobOutcome
{
    private protected CiFailureJobOutcome() { }
}

/// <summary>The run either hadn't failed (<see cref="CiFailureSkipped"/>) or couldn't be checked
/// (<see cref="CiFailureError"/>) — never <see cref="CiFailureDetected"/>, which always leads to
/// <see cref="CiFailureJobRan"/> instead.</summary>
internal sealed record CiFailureJobNotRun(ICiFailureResult Reason) : CiFailureJobOutcome;

internal sealed record CiFailureJobRan(IJobResult Result) : CiFailureJobOutcome;
