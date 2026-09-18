using Rix.Job;
using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>
/// Checks whether a workflow run failed and, only if it did, runs the coding agent against the
/// prompt built from that failure — the full pipeline behind <c>rix ci-failure</c>. Thin
/// orchestration over <see cref="CiFailureDetector.DetectAsync"/> and <see cref="JobRunner.RunAsync"/>;
/// neither is duplicated here.
/// </summary>
internal static class CiFailureRunner
{
    internal static async Task<CiFailureOutcome> RunAsync
    (
        CiFailureConfig config,
        CiFailureContext context,
        CancellationToken cancellationToken
    )
    {
        var detection = await CiFailureDetector.DetectAsync(config.Repo, config.RunId, context.CiFailureHost, config.MaxRixCommits, cancellationToken);
        if (detection is not CiFailureDetected detected)
            return new CiFailureNotRun(detection);

        // Resuming a CI failure means pushing a fix back onto the exact branch that failed - the
        // only sensible /push target here, so it's derived from the detected run rather than
        // accepted as a caller-supplied input (see CiFailureConfig.ToJobConfig). That branch
        // already exists on the remote regardless of whether it happens to be rix/*-named (e.g. CI
        // failed on a human's own branch, not a previous rix run), so it's always allowed.
        var job = config.ToJobConfig(detected.Prompt, new BranchName(detected.Branch));
        var jobResult = await JobRunner.RunAsync(job, context.Job, cancellationToken);
        return new CiFailureRan(job, jobResult);
    }
}

/// <summary>Whether <see cref="CiFailureRunner.RunAsync"/> ran the agent at all.</summary>
internal abstract record CiFailureOutcome
{
    private protected CiFailureOutcome() { }
}

/// <summary>The run either hadn't failed (<see cref="CiFailureSkipped"/>), had failed on a fork's
/// branch that rix may not answer (<see cref="CiFailureUntrustedRun"/>), had failed on a branch rix
/// has already been fixing on its own (<see cref="CiFailureLoopGuarded"/>), or couldn't be checked
/// (<see cref="CiFailureError"/>) — never <see cref="CiFailureDetected"/>, which always
/// leads to <see cref="CiFailureRan"/> instead.</summary>
internal sealed record CiFailureNotRun(ICiFailureResult Reason) : CiFailureOutcome;

/// <summary>The agent ran. Carries the <see cref="JobConfig"/> it ran under, which only exists once
/// a failure has been detected, so the caller can report the outcome against the same config
/// instead of building a promptless stand-in for it.</summary>
internal sealed record CiFailureRan(JobConfig Job, IJobResult Result) : CiFailureOutcome;
