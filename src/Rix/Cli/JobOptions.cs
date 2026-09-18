using Rix.Job;
using System.CommandLine;
// Aliased because rix has its own ParseResult<T> (the value-object parse outcome) in scope here.
using CliParseResult = System.CommandLine.Parsing.ParseResult;

namespace Rix.Cli;

/// <summary>The CLI options every <see cref="JobInputs"/> field is read from, shared by <c>job</c>
/// and <c>ci-failure</c>, which both run the coding agent and so take the same execution
/// parameters. <see cref="PromptOption"/> and <see cref="AllowedPushBranchesOption"/> are the
/// exception: <c>ci-failure</c> derives both from the failure it detects rather than accepting them
/// as inputs, so <see cref="AddTo"/> and <see cref="ReadInputs"/> leave those two to <c>job</c>.</summary>
internal static class JobOptions
{
    internal static readonly Option<string> RepoOption = new
    (
        name: "--repo",
        description: "Full GitHub repo identifier (owner/repo)"
    )
    { IsRequired = false };

    internal static readonly Option<string> ReadTokenOption = new
    (
        name: "--read-token",
        description: "GitHub PAT with read access to the repo, including Actions:read"
    )
    { IsRequired = false };

    internal static readonly Option<string> PromptOption = new
    (
        name: "--prompt",
        description: "Task prompt passed to the coding agent"
    )
    { IsRequired = false };

    internal static readonly Option<string> MaxTokensOption = new
    (
        name: "--max-tokens",
        description: $"Coding agent token budget cap (default: {JobConfig.DefaultMaxTokens})"
    )
    { IsRequired = false };

    internal static readonly Option<string> TimeoutOption = new
    (
        name: "--timeout",
        description: $"Wall-clock timeout in minutes (default: {JobConfig.DefaultTimeoutMinutes})"
    )
    { IsRequired = false };

    internal static readonly Option<string> WorkDirOption = new
    (
        name: "--work-dir",
        description: "Base directory for the temp clone (default: system temp)"
    )
    { IsRequired = false };

    internal static readonly Option<string> OutputDirOption = new
    (
        name: "--output-dir",
        description: "Directory where result.json and git bundles are written"
    )
    { IsRequired = false };

    internal static readonly Option<string> AgentOption = new
    (
        name: "--agent",
        description: "Coding agent to run: 'opencode' (default), 'claude', or 'pi'"
    )
    { IsRequired = false };

    internal static readonly Option<string> ModelOption = new
    (
        name: "--model",
        description: "Model identifier passed to the agent CLI (e.g. 'openai/gpt-4o' for opencode). " +
            "Provider-specific; forwarded verbatim. Omit to use the agent CLI's own default model."
    )
    { IsRequired = false };

    internal static readonly Option<string> AgentApiKeyOption = new
    (
        name: "--agent-api-key",
        description: "API key for the selected agent's model provider; optional (opencode's free default model needs none)"
    )
    { IsRequired = false };

    internal static readonly Option<string> AgentApiKeyEnvOption = new
    (
        name: "--agent-api-key-env",
        description: "Name of the environment variable agent-api-key is exported as to the agent CLI " +
            "(e.g. OPENCODE_API_KEY, ANTHROPIC_API_KEY, OPENAI_API_KEY, AWS_ACCESS_KEY_ID) — whatever the " +
            "selected provider/model expects. Must end in a credential-shaped suffix (_API_KEY, _TOKEN, _ACCESS_KEY_ID, etc). " +
            "Omit to use a default based on agent: OPENCODE_API_KEY for opencode, ANTHROPIC_API_KEY for claude. " +
            "Required for pi whenever agent-api-key is set, since pi has no single default provider to fall back on."
    )
    { IsRequired = false };

    internal static readonly Option<string> AllowedPushBranchesOption = new
    (
        name: "--allowed-push-branches",
        description: "Comma-separated list of branches the /push API endpoint may deliver to " +
            "(default: none — /push is disabled until this is set)"
    )
    { IsRequired = false };

    /// <summary>Registers every option <see cref="ReadInputs"/> reads, so the two can't drift: a
    /// new job option is added here once and both commands accept it.</summary>
    internal static void AddTo(Command command)
    {
        command.AddOption(RepoOption);
        command.AddOption(ReadTokenOption);
        command.AddOption(MaxTokensOption);
        command.AddOption(TimeoutOption);
        command.AddOption(WorkDirOption);
        command.AddOption(OutputDirOption);
        command.AddOption(AgentOption);
        command.AddOption(ModelOption);
        command.AddOption(AgentApiKeyOption);
        command.AddOption(AgentApiKeyEnvOption);
    }

    /// <summary>Reads the options <see cref="AddTo"/> registered, each falling back to its
    /// environment variable. Leaves <see cref="JobInputs.Prompt"/> and
    /// <see cref="JobInputs.AllowedPushBranches"/> unset for the caller to supply — <c>job</c> from
    /// its own two options, <c>ci-failure</c> from the failure it detects.</summary>
    internal static JobInputs ReadInputs(CliParseResult parsed) => new
    (
        Repo:           parsed.Str(RepoOption,      "RIX_REPO"),
        ReadToken:      parsed.Str(ReadTokenOption, "RIX_READ_TOKEN"),
        MaxTokens:      parsed.Str(MaxTokensOption, "RIX_MAX_TOKENS"),
        TimeoutMinutes: parsed.Str(TimeoutOption,   "RIX_TIMEOUT"),
        WorkDir:        parsed.Str(WorkDirOption,   "RIX_WORK_DIR"),
        OutputDir:      parsed.Str(OutputDirOption, "RIX_OUTPUT_DIR"),
        Agent:          parsed.Str(AgentOption,     "RIX_AGENT"),
        Model:          parsed.Str(ModelOption,     "RIX_MODEL"),
        AgentApiKey:    parsed.Str(AgentApiKeyOption,    "AGENT_API_KEY"),
        AgentApiKeyEnv: parsed.Str(AgentApiKeyEnvOption, "AGENT_API_KEY_ENV")
    );
}
