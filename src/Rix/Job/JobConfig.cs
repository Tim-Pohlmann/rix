using Rix.Agents;

namespace Rix.Job;

/// <summary>Everything <c>rix job</c> needs, already strongly typed: every field is a value object
/// that validated itself on construction, so a <see cref="JobConfig"/> can't exist in an invalid
/// state and nothing downstream re-checks it. Turning raw CLI/environment text into these values
/// is the CLI layer's job (see <see cref="Cli.JobOptions"/>), which reads each option straight
/// into the value the constructor takes.</summary>
/// <param name="AllowedPushBranches">The only branches <c>/push</c> may deliver to. Empty means
/// <c>/push</c> is disabled — an operator opts in by naming the branches this run may touch. Any
/// branch name is acceptable (unlike <c>rix/*</c>-restricted branches the agent creates via
/// <c>/pr</c>), since these already exist on the remote before the job ever runs.</param>
/// <param name="AgentHome">Optional agent home files: when set, a directory from another repo is
/// copied into the runner's home before the agent starts. <c>null</c> (the
/// default) means no <c>--factory-repo</c> was given and the runner home is left untouched.</param>
internal record JobConfig
(
    RepoIdentifier Repo,
    GitReadToken ReadToken,
    TimeoutMinutes TimeoutMinutes,
    // Reordering these two relative to each other compiles at every call site and silently swaps
    // the clone's location with the results' - they share a type, so nothing catches it. That is
    // why call sites pass them by name while the rest of the list stays positional.
    DirectoryPath WorkDir,
    DirectoryPath OutputDir,
    AgentConfig Agent,
    IReadOnlyList<BranchName> AllowedPushBranches,
    AgentHomeInfo? AgentHome = null
)
{
    internal const int DefaultMaxTokens = 50_000;
    internal const int DefaultTimeoutMinutes = 30;
    internal const AgentKind DefaultAgent = AgentKind.OpenCode;

    /// <summary>Directory inside the factory repo whose contents are copied into the runner home
    /// when <c>--agent-home-path</c> is omitted but <c>--factory-repo</c> is set.</summary>
    internal const string DefaultAgentHomePath = ".rix/agent-home";
}

/// <summary>How the coding agent should be run: which agent (<see cref="AgentKind"/>), the task
/// <paramref name="Prompt"/> it receives, its token budget, and an optional <paramref name="Model"/>
/// identifier forwarded verbatim to the agent CLI (e.g. <c>openai/gpt-4o</c> for opencode) — rix
/// does not interpret or validate it, since which providers/models an agent CLI accepts is entirely
/// that CLI's concern. Groups the inputs the <c>--agent</c>, <c>--prompt</c>, <c>--max-tokens</c>,
/// and <c>--model</c> flags configure.
/// <paramref name="Credential"/> (already resolved and validated by
/// <see cref="AgentCredential.Resolve"/>) is <see cref="JobRunner"/>'s instruction for which single
/// env var to add to the agent invocation's <see cref="AgentInvocation.EnvironmentOverrides"/> —
/// null when no key was supplied, and carrying both halves whenever it is not.</summary>
internal sealed record AgentConfig
(
    AgentKind Kind,
    string Prompt,
    MaxTokens MaxTokens,
    string? Model = null,
    AgentCredential? Credential = null
);

/// <summary>Where the agent home files come from and where they go: the contents of the
/// repo-relative <paramref name="SourcePath"/> directory in <paramref name="Repo"/> are copied into
/// <paramref name="Home"/>, the runner user's home, before the agent starts.</summary>
internal sealed record AgentHomeInfo(RepoIdentifier Repo, RepoRelativePath SourcePath, DirectoryPath Home);
