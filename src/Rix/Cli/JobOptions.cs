using Rix.Agents;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Cli;

/// <summary>CLI options shared by <c>job</c> and <c>ci-failure-job</c>, which both run the coding
/// agent and so take the same execution parameters.</summary>
internal static class JobOptions
{
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

    /// <summary>Turns the shared options (plus the values the two commands source differently:
    /// <paramref name="repo"/>/<paramref name="readToken"/> from their own flags,
    /// <paramref name="prompt"/> and <paramref name="allowedPushBranches"/> only meaningful to
    /// <c>job</c>) into a <see cref="JobConfig"/>. The first malformed value throws
    /// <see cref="InvalidInputException"/>, which <see cref="CliPipeline"/> reports.</summary>
    internal static JobConfig ReadConfig
    (
        ParseResult parsed,
        RepoIdentifier repo,
        GitReadToken readToken,
        string prompt,
        IReadOnlyList<BranchName> allowedPushBranches
    )
    {
        var agent = Input.Optional("--agent", parsed.Str(AgentOption, "RIX_AGENT"), AgentKindParser.Parse, JobConfig.DefaultAgent);
        var maxTokens = Input.Optional("--max-tokens", parsed.Str(MaxTokensOption, "RIX_MAX_TOKENS"), Input.Positive<int>, JobConfig.DefaultMaxTokens);
        var timeout = Input.Optional("--timeout", parsed.Str(TimeoutOption, "RIX_TIMEOUT"), Input.Positive<int>, JobConfig.DefaultTimeoutMinutes);
        var workDir = Input.Optional("--work-dir", parsed.Str(WorkDirOption, "RIX_WORK_DIR"), path => new DirectoryPath(path), new DirectoryPath(Path.GetTempPath()));
        var outputDir = Input.Required("--output-dir", parsed.Str(OutputDirOption, "RIX_OUTPUT_DIR"), path => new DirectoryPath(path));
        var model = Input.OptionalText(parsed.Str(ModelOption, "RIX_MODEL"));

        // No key is required when model is left unset - opencode then picks its own free model.
        // The env var name is only resolved (and validated) once a key actually needs exporting.
        var apiKey = Input.OptionalText(parsed.Str(AgentApiKeyOption, "AGENT_API_KEY"));
        var apiKeyEnv = apiKey switch
        {
            null => null,
            _ => Input.Named("--agent-api-key-env", () => AgentCredential.ResolveEnvName(agent, parsed.Str(AgentApiKeyEnvOption, "AGENT_API_KEY_ENV"))),
        };

        return new JobConfig
        (
            Repo: repo,
            ReadToken: readToken,
            TimeoutMinutes: new TimeoutMinutes(timeout),
            WorkDir: workDir,
            OutputDir: outputDir,
            Agent: new AgentConfig(agent, prompt, new MaxTokens(maxTokens), model, apiKey, apiKeyEnv),
            AllowedPushBranches: allowedPushBranches
        );
    }
}
