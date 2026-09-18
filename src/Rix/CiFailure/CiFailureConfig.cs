using Rix.Job;

namespace Rix.CiFailure;

/// <summary>Everything <c>rix ci-failure</c> needs, already strongly typed: the <see cref="RunId"/>
/// this command adds on top of the <see cref="JobSettings"/> it shares with <c>rix job</c>. The
/// agent's prompt and its <c>/push</c> allow-list are deliberately absent — both describe a
/// failure that hasn't been detected yet, so <see cref="ToJobConfig"/> completes the
/// <see cref="JobConfig"/> only once it has.</summary>
internal sealed record CiFailureConfig(RunId RunId, JobSettings Job)
{
    /// <summary>The repo whose run is inspected — the same one the agent then clones, derived from
    /// the job settings rather than supplied alongside them so the two can't describe different
    /// repos.</summary>
    internal RepoIdentifier Repo => Job.Repo;

    /// <summary>The token the run is read with, shared with the job's clone for the same reason as
    /// <see cref="Repo"/>.</summary>
    internal GitReadToken ReadToken => Job.ReadToken;

    /// <summary>Builds the agent-running half of this config, now that a failure has actually been
    /// detected and both missing pieces are known: the <paramref name="prompt"/> describing the
    /// failure, and <paramref name="allowedPushBranch"/>, the failing run's own branch — the only
    /// branch a fix for it could sensibly be pushed to, which is why it's derived here rather than
    /// accepted as a caller-supplied input. The branch is handed over as a one-element allow-list
    /// rather than round-tripped through <c>--allowed-push-branches</c>' comma-separated form,
    /// which would split a branch name containing a comma into two entries — permitting pushes to
    /// branches that merely share those halves while rejecting the failing branch itself.</summary>
    internal JobConfig ToJobConfig(string prompt, BranchName allowedPushBranch)
    => Job.ToJob(prompt, [allowedPushBranch]);
}
