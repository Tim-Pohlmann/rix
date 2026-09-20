using Rix.Agents;
using Rix.Job;

namespace Rix.CiFailure;

/// <summary>Everything <c>rix ci-failure</c> needs, already strongly typed: the <see cref="RunId"/>
/// this command adds on top of the job values it shares with <c>rix job</c>. Those are held as
/// plain fields rather than as a <see cref="JobConfig"/>, because a <see cref="JobConfig"/> can't
/// exist without the agent's prompt and its <c>/push</c> allow-list — both describe a failure that
/// hasn't been detected yet — so <see cref="ToJobConfig"/> builds it only once it has.</summary>
/// <param name="Repo">The repo whose run is inspected — and the one the agent then clones, so the
/// two can't describe different repos.</param>
/// <param name="ReadToken">The token the run is read with, and the job's clone credential.</param>
internal sealed record CiFailureConfig
(
    RunId RunId,
    RepoIdentifier Repo,
    GitReadToken ReadToken,
    TimeoutMinutes TimeoutMinutes,
    DirectoryPath WorkDir,
    DirectoryPath OutputDir,
    AgentKind Agent,
    MaxTokens MaxTokens,
    MaxRixCommits MaxRixCommits,
    string? Model = null,
    AgentCredential? Credential = null
)
{
    /// <summary>How many of rix's own commits may already sit at a failing branch's tip before
    /// <c>ci-failure</c> leaves it alone. Five rather than one because rix fixing its own last
    /// attempt is the normal case, not the pathological one - the first attempt failing is exactly
    /// why there is a second - and rather than unbounded because nothing else ever stops a branch
    /// that fails the same way every time. A single agent run can produce more than one commit, so
    /// this bounds commits, not attempts: the effective number of attempts is at most this.</summary>
    internal const int DefaultMaxRixCommits = 5;

    /// <summary>Builds the agent-running half of this config, now that a failure has actually been
    /// detected and both missing pieces are known: the <paramref name="prompt"/> describing the
    /// failure, and <paramref name="allowedPushBranch"/>, the failing run's own branch — the only
    /// branch a fix for it could sensibly be pushed to, which is why it's derived here rather than
    /// accepted as a caller-supplied input. The branch is handed over as a one-element allow-list
    /// rather than round-tripped through <c>--allowed-push-branches</c>' comma-separated form,
    /// which would split a branch name containing a comma into two entries — permitting pushes to
    /// branches that merely share those halves while rejecting the failing branch itself.</summary>
    internal JobConfig ToJobConfig(string prompt, BranchName allowedPushBranch)
    => new
    (
        Repo: Repo,
        ReadToken: ReadToken,
        TimeoutMinutes: TimeoutMinutes,
        WorkDir: WorkDir,
        OutputDir: OutputDir,
        Agent: new AgentConfig(Agent, prompt, MaxTokens, Model, Credential),
        AllowedPushBranches: [allowedPushBranch]
    );
}
