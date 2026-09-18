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
    string? Model = null,
    string? ApiKey = null,
    string? ApiKeyEnv = null
)
{
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
        Agent: new AgentConfig(Agent, prompt, MaxTokens, Model, ApiKey, ApiKeyEnv),
        AllowedPushBranches: [allowedPushBranch]
    );
}
