using Rix.Agents;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Cli;

/// <summary>The CLI options shared by <c>job</c> and <c>ci-failure</c>, which both run the coding
/// agent and so take the same execution parameters, plus one reader per option that turns its
/// flag-or-environment text into the value the command's config takes — the first missing or
/// malformed value throws <see cref="InvalidInputException"/> naming the flag, which
/// <see cref="CliPipeline"/> reports. Each command assembles its own config from these at the
/// call site, in the order it wants problems reported. <see cref="PromptOption"/> and
/// <see cref="AllowedPushBranchesOption"/> are the exception: <c>ci-failure</c> derives both from
/// the failure it detects rather than accepting them as inputs, so <see cref="AddTo"/> leaves
/// those two to <c>job</c>.</summary>
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

    /// <summary>Registers every shared option, so a new one is added here once and both commands
    /// accept it — each command's handler then reads it via the matching reader below.</summary>
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

    internal static RepoIdentifier ReadRepo(ParseResult parsed)
    => Input.Required("--repo", parsed.Str(RepoOption, "RIX_REPO"), value => new RepoIdentifier(value));

    internal static GitReadToken ReadReadToken(ParseResult parsed)
    => Input.Required("--read-token", parsed.Str(ReadTokenOption, "RIX_READ_TOKEN"), value => new GitReadToken(value));

    internal static AgentKind ReadAgent(ParseResult parsed)
    => Input.Optional("--agent", parsed.Str(AgentOption, "RIX_AGENT"), AgentKindParser.Parse, JobConfig.DefaultAgent);

    internal static MaxTokens ReadMaxTokens(ParseResult parsed)
    => new(Input.Optional("--max-tokens", parsed.Str(MaxTokensOption, "RIX_MAX_TOKENS"), Input.Positive<int>, JobConfig.DefaultMaxTokens));

    internal static TimeoutMinutes ReadTimeout(ParseResult parsed)
    => new(Input.Optional("--timeout", parsed.Str(TimeoutOption, "RIX_TIMEOUT"), Input.Positive<int>, JobConfig.DefaultTimeoutMinutes));

    internal static DirectoryPath ReadWorkDir(ParseResult parsed)
    => Input.Optional("--work-dir", parsed.Str(WorkDirOption, "RIX_WORK_DIR"), path => new DirectoryPath(path), new DirectoryPath(Path.GetTempPath()));

    internal static DirectoryPath ReadOutputDir(ParseResult parsed)
    => Input.Required("--output-dir", parsed.Str(OutputDirOption, "RIX_OUTPUT_DIR"), path => new DirectoryPath(path));

    internal static string? ReadModel(ParseResult parsed)
    => Input.OptionalText(parsed.Str(ModelOption, "RIX_MODEL"));

    /// <summary>No key is required when <c>--model</c> is left unset - opencode then picks its own
    /// free model - so this is simply <c>null</c> when nothing was supplied.</summary>
    internal static string? ReadAgentApiKey(ParseResult parsed)
    => Input.OptionalText(parsed.Str(AgentApiKeyOption, "AGENT_API_KEY"));

    /// <summary>The env var name is only resolved (and validated) once there is actually a
    /// <paramref name="apiKey"/> to export, and its default depends on <paramref name="agent"/>,
    /// so both are read first and passed in rather than re-read here.</summary>
    internal static string? ReadAgentApiKeyEnv(ParseResult parsed, AgentKind agent, string? apiKey)
    => apiKey switch
    {
        null => null,
        _ => Input.Named("--agent-api-key-env", () => AgentCredential.ResolveEnvName(agent, parsed.Str(AgentApiKeyEnvOption, "AGENT_API_KEY_ENV"))),
    };
}
