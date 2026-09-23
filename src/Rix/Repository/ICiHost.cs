namespace Rix.Repository;

/// <summary>The read-only operations against whatever ran the build: what one run's outcome was,
/// and what its failing jobs logged. Separate from <see cref="ICiFailureRepoHost"/> because who
/// hosts the repo and who runs the CI are independently chosen in practice — GitHub repos built by
/// Buildkite, CircleCI or Jenkins are ordinary setups — so a second CI provider should be one new
/// implementation of this, not a second copy of every repo operation alongside it.
///
/// Stated in the vocabulary every CI system shares rather than in any one's: a run, its outcome,
/// the logs of the jobs that failed. What a provider calls those, and which REST fields they arrive
/// in, stops at its implementation.</summary>
internal interface ICiHost
{
    Task<CiRun> GetRunAsync(RunId runId, CancellationToken cancellationToken);

    /// <summary>Builds an excerpt of the run's failed jobs' logs, each job's tail under a heading
    /// naming it, in at most <paramref name="totalTailChars"/> characters however many jobs failed.
    /// The caller owns that budget because it is the one that has to fit the excerpt into a prompt,
    /// and owns it in full rather than per job so the size it asks for is the size it gets; passing
    /// it down means the bytes beyond it are dropped as they arrive instead of after a whole
    /// multi-MB log is in memory. How the budget is divided, and how many jobs are worth covering
    /// before each share is too small to read, is the implementation's call.</summary>
    Task<string> GetFailedJobLogsAsync(RunId runId, int totalTailChars, CancellationToken cancellationToken);
}

/// <summary>The facts about one CI run needed to describe why it failed, and to decide whether it
/// may be answered at all. Typed rather than a bag of strings, so the rules for comparing a repo
/// identity live on <see cref="RepoIdentifier"/> and the rules for what counts as a failure live on
/// <see cref="CiOutcome"/>, instead of being restated by everything that reads a run.</summary>
/// <param name="HeadRepo">The repo the run's branch lives in, which is the fork rather than the
/// watched repo when the run belongs to a fork's pull request.</param>
internal sealed record CiRun
(
    CiOutcome Outcome,
    string Title,
    string Url,
    BranchName HeadBranch,
    RepoIdentifier HeadRepo
);

/// <summary>How a CI run ended. A closed set of the outcomes every CI system has, plus
/// <see cref="CiOtherOutcome"/> for whatever else a provider reports, so reading one is a match that
/// can't silently miss a case the way comparing a raw status word can — a provider spelling failure
/// differently would have shown up as "not a failure" and been skipped in silence.
///
/// <see cref="Name"/> is carried per case rather than derived by the reader because the only thing
/// anyone does with a non-failure outcome is say what it was, and a provider's own word for it
/// (<c>timed_out</c>, <c>startup_failure</c>) is more use in that sentence than "other" would
/// be.</summary>
internal abstract record CiOutcome
{
    private protected CiOutcome() { }

    /// <summary>The one word this outcome is reported as, where rix explains why it did nothing.</summary>
    internal abstract string Name { get; }
}

/// <summary>The run failed — the one outcome <c>rix ci-failure</c> acts on.</summary>
internal sealed record CiFailed : CiOutcome
{
    internal override string Name => "failed";
}

internal sealed record CiSucceeded : CiOutcome
{
    internal override string Name => "succeeded";
}

internal sealed record CiCancelled : CiOutcome
{
    internal override string Name => "cancelled";
}

/// <summary>The run hasn't finished, so it has no outcome yet. Distinct from
/// <see cref="CiOtherOutcome"/>: there is no word to report, because the provider hasn't said
/// anything yet rather than having said something unfamiliar.</summary>
internal sealed record CiPending : CiOutcome
{
    internal override string Name => "pending";
}

/// <summary>Any outcome outside the shared set, carrying the provider's own word for it — the run
/// timed out, was skipped, needs an action, went stale. None of them is a failure rix answers, and
/// all of them are worth naming exactly when saying so.</summary>
internal sealed record CiOtherOutcome(string Raw) : CiOutcome
{
    internal override string Name => Raw;
}
