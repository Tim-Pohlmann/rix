using Rix.Agents;

namespace Rix.Job;

/// <summary>Everything <c>rix job</c> needs, already strongly typed: every field is a value object
/// that validated itself on construction, so a <see cref="JobConfig"/> can't exist in an invalid
/// state and nothing downstream re-checks it. Turning raw CLI/environment text into these values
/// is the CLI layer's job (see <see cref="Cli.JobOptions"/>).</summary>
/// <param name="AllowedPushBranches">The only branches <c>/push</c> may deliver to. Empty means
/// <c>/push</c> is disabled — an operator opts in by naming the branches this run may touch. Any
/// branch name is acceptable (unlike <c>rix/*</c>-restricted branches the agent creates via
/// <c>/pr</c>), since these already exist on the remote before the job ever runs.</param>
internal record JobConfig
(
    RepoIdentifier Repo,
    GitReadToken ReadToken,
    TimeoutMinutes TimeoutMinutes,
    DirectoryPath WorkDir,
    DirectoryPath OutputDir,
    AgentConfig Agent,
    IReadOnlyList<BranchName> AllowedPushBranches
)
{
    internal const int DefaultMaxTokens = 50_000;
    internal const int DefaultTimeoutMinutes = 30;
    internal const AgentKind DefaultAgent = AgentKind.OpenCode;

    /// <summary>Returns a copy of this config with <paramref name="prompt"/> substituted for the
    /// agent's task prompt. Used by <c>rix ci-failure-job</c>, where the real prompt is only known
    /// once the CI-failure check actually finds a failure — everything else is built up front
    /// around a placeholder prompt. <paramref name="prompt"/> itself is never blank in practice
    /// (it's always built by <see cref="CiFailure.CiFailureRunner"/> from a fixed template, not
    /// raw external input), but the check below still guards the invariant the CLI enforces for
    /// any other caller-supplied prompt.</summary>
    internal JobConfig WithPrompt(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return this with { Agent = Agent with { Prompt = prompt } };
    }

    /// <summary>Returns a copy of this config with <paramref name="allowedPushBranches"/>
    /// substituted for the <c>/push</c> allow-list. Used by <c>rix ci-failure-job</c>, which
    /// derives the allow-list itself from the branch whose CI actually failed once that's known,
    /// rather than accepting it as a caller-supplied input — the whole point of resuming a CI
    /// failure is pushing a fix back onto that exact branch, so letting a caller widen the
    /// allow-list to unrelated branches would undermine the restriction rather than configure
    /// it.</summary>
    internal JobConfig WithAllowedPushBranches(IReadOnlyList<BranchName> allowedPushBranches)
    => this with { AllowedPushBranches = allowedPushBranches };
}

/// <summary>How the coding agent should be run: which agent (<see cref="AgentKind"/>), the task
/// <paramref name="Prompt"/> it receives, its token budget, and an optional <paramref name="Model"/>
/// identifier forwarded verbatim to the agent CLI (e.g. <c>openai/gpt-4o</c> for opencode) — rix
/// does not interpret or validate it, since which providers/models an agent CLI accepts is entirely
/// that CLI's concern. Groups the inputs the <c>--agent</c>, <c>--prompt</c>, <c>--max-tokens</c>,
/// and <c>--model</c> flags configure.
/// <paramref name="ApiKey"/> and <paramref name="ApiKeyEnv"/> (already resolved and validated by
/// <see cref="AgentCredential.ResolveEnvName"/>) are <see cref="JobRunner"/>'s instructions for
/// which single env var to add to the agent invocation's <see cref="AgentInvocation.EnvironmentOverrides"/>
/// — never null together, and never both null unless no key was supplied at all.</summary>
internal sealed record AgentConfig
(
    AgentKind Kind,
    string Prompt,
    MaxTokens MaxTokens,
    string? Model = null,
    string? ApiKey = null,
    string? ApiKeyEnv = null
);
