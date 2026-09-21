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
/// those two to <c>job</c>. <c>--repo</c> and <c>--work-dir</c> live in <see cref="CommonOptions"/>
/// instead, since <c>submit</c> takes them too without taking anything else here;
/// <see cref="AddTo"/> still registers them, so a command accepting the agent-running set keeps
/// getting the whole flag surface from one call.</summary>
internal static class JobOptions
{
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
        command.AddOption(CommonOptions.RepoOption);
        command.AddOption(ReadTokenOption);
        command.AddOption(MaxTokensOption);
        command.AddOption(TimeoutOption);
        command.AddOption(CommonOptions.WorkDirOption);
        command.AddOption(OutputDirOption);
        command.AddOption(AgentOption);
        command.AddOption(ModelOption);
        command.AddOption(AgentApiKeyOption);
        command.AddOption(AgentApiKeyEnvOption);
    }

    internal static GitReadToken ReadReadToken(ParseResult parsed)
    => parsed.Required(ReadTokenOption, "RIX_READ_TOKEN", value => new GitReadToken(value));

    internal static AgentKind ReadAgent(ParseResult parsed)
    => parsed.Optional(AgentOption, "RIX_AGENT", AgentKindParser.Parse, JobConfig.DefaultAgent);

    /// <summary>Constructing inside the reader's callback, rather than around it, is what puts the
    /// constructor's complaint under the flag: <see cref="Input.Named{T}"/> only prefixes what runs
    /// within it, so <c>new MaxTokens(...)</c> on the outside would answer <c>--max-tokens 0</c>
    /// with a bare "must be a positive integer, got '0'". Same for the reader below.</summary>
    internal static MaxTokens ReadMaxTokens(ParseResult parsed)
    => parsed.Optional
    (
        MaxTokensOption,
        "RIX_MAX_TOKENS",
        raw => new MaxTokens(Input.WholeNumber<int>(raw)),
        new MaxTokens(JobConfig.DefaultMaxTokens)
    );

    internal static TimeoutMinutes ReadTimeout(ParseResult parsed)
    => parsed.Optional
    (
        TimeoutOption,
        "RIX_TIMEOUT",
        raw => new TimeoutMinutes(Input.WholeNumber<int>(raw)),
        new TimeoutMinutes(JobConfig.DefaultTimeoutMinutes)
    );

    internal static DirectoryPath ReadOutputDir(ParseResult parsed)
    => parsed.Required(OutputDirOption, "RIX_OUTPUT_DIR", path => new DirectoryPath(path));

    internal static string? ReadModel(ParseResult parsed)
    => parsed.OptionalText(ModelOption, "RIX_MODEL");

    /// <summary>No key is required when <c>--model</c> is left unset - opencode then picks its own
    /// free model - so this is simply <c>null</c> when nothing was supplied.</summary>
    internal static string? ReadAgentApiKey(ParseResult parsed)
    => parsed.OptionalText(AgentApiKeyOption, "AGENT_API_KEY");

    /// <summary>Whether the name is needed at all, and what it defaults to, both depend on values
    /// read from other flags, so <paramref name="agent"/> and <paramref name="apiKey"/> are passed
    /// in rather than re-read here. <see cref="AgentCredential.ResolveEnvName"/> owns both
    /// rules; this only supplies the raw flag text and the flag name any complaint is reported
    /// under.</summary>
    internal static string? ReadAgentApiKeyEnv(ParseResult parsed, AgentKind agent, string? apiKey)
    => parsed.Named(AgentApiKeyEnvOption, "AGENT_API_KEY_ENV", raw => AgentCredential.ResolveEnvName(agent, apiKey, raw));
}
