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
}

/// <summary>A <see cref="JobConfig"/> minus the two values that describe the task rather than how
/// to run it: the agent's prompt and the <c>/push</c> allow-list. Everything the job options
/// shared by <c>job</c> and <c>ci-failure</c> determine on their own (see
/// <see cref="Cli.JobOptions.ReadSettings"/>). Exists because <c>ci-failure</c> reads those
/// options before it knows whether it will run the agent at all — the prompt and the branch it may
/// push to only exist once a failure has been detected — so it holds these until
/// <see cref="ToJob"/> can complete them, rather than building a throwaway <see cref="JobConfig"/>
/// around a placeholder prompt.</summary>
internal sealed record JobSettings
(
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
    /// <summary>Completes these settings into a runnable <see cref="JobConfig"/> with the task
    /// <paramref name="prompt"/> and the branches <c>/push</c> may deliver to. A blank prompt is a
    /// programming error rather than an input error: every caller either required it from the user
    /// already or built it from a fixed template.</summary>
    internal JobConfig ToJob(string prompt, IReadOnlyList<BranchName> allowedPushBranches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return new JobConfig
        (
            Repo: Repo,
            ReadToken: ReadToken,
            TimeoutMinutes: TimeoutMinutes,
            WorkDir: WorkDir,
            OutputDir: OutputDir,
            Agent: new AgentConfig(Agent, prompt, MaxTokens, Model, ApiKey, ApiKeyEnv),
            AllowedPushBranches: allowedPushBranches
        );
    }
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
/// — both null when no key was supplied, otherwise both set; never one without the other.</summary>
internal sealed record AgentConfig
(
    AgentKind Kind,
    string Prompt,
    MaxTokens MaxTokens,
    string? Model = null,
    string? ApiKey = null,
    string? ApiKeyEnv = null
);
