namespace Rix.CiFailure;

/// <summary>Everything <c>rix ci-failure</c> needs, already strongly typed. Detection only: the
/// agent-running values <c>rix job</c> takes (agent, model, credential, token budget, timeout, a
/// working directory to clone into) are deliberately absent, because this command never starts an
/// agent — it reports what it found and stops, leaving the run to a separate job that cannot reach
/// back into the machine this check ran on.</summary>
/// <param name="Repo">The repo whose run is inspected, and the one the run's branch is judged
/// against — a run whose head lives elsewhere is a fork's.</param>
/// <param name="ReadToken">The token the run's metadata, logs and commits are read with.</param>
/// <param name="OutputDir">Where <c>result.json</c> and, for a detected failure,
/// <c>prompt.md</c> are written.</param>
internal sealed record CiFailureConfig
(
    RunId RunId,
    RepoIdentifier Repo,
    GitReadToken ReadToken,
    DirectoryPath OutputDir,
    MaxRixCommits MaxRixCommits
)
{
    /// <summary>How many of rix's own commits may already sit at a failing branch's tip before
    /// <c>ci-failure</c> leaves it alone. Five rather than one because rix fixing its own last
    /// attempt is the normal case, not the pathological one - the first attempt failing is exactly
    /// why there is a second - and rather than unbounded because nothing else ever stops a branch
    /// that fails the same way every time. A single agent run can produce more than one commit, so
    /// this bounds commits, not attempts: the effective number of attempts is at most this.
    ///
    /// A bound on cost and noise rather than a security control: the streak is counted from commit
    /// authorship, which whoever wrote those commits chose. An agent that wanted more turns could
    /// author one as a human and clear the count.</summary>
    internal const int DefaultMaxRixCommits = 5;
}
