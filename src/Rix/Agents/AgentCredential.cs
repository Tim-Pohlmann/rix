using System.Text.RegularExpressions;

namespace Rix.Agents;

/// <summary>
/// Resolves the environment variable name an <c>--agent-api-key</c> should be exported as for the
/// child agent CLI process, and validates it. Resolved by the CLI when building a
/// <see cref="Job.JobConfig"/> so the (name, value) pair can be attached to the run's
/// <see cref="AgentInvocation.EnvironmentOverrides"/>
/// — the child process gets the credential without it ever needing to exist under that name in
/// rix's own process environment.
/// </summary>
internal static partial class AgentCredential
{
    /// <summary>
    /// Restricts env var names to credential-shaped suffixes covering opencode's supported
    /// providers (ANTHROPIC_API_KEY, AWS_ACCESS_KEY_ID, GOOGLE_APPLICATION_CREDENTIALS,
    /// SNOWFLAKE_CORTEX_TOKEN, ...), rather than deny-listing every internal/runtime variable a
    /// caller could otherwise clobber. A couple of the allowed suffixes (e.g. _TOKEN) would
    /// otherwise overlap with vars rix's own plumbing relies on, so the leading negative lookahead
    /// also blocks RIX_*/AGENT_API_KEY* (rix's own runtime vars) and GITHUB_* (GITHUB_TOKEN etc)
    /// by name.
    /// </summary>
    [GeneratedRegex(@"^(?!RIX_|AGENT_API_KEY|GITHUB_)[A-Z][A-Z0-9_]*_(API_KEY|TOKEN|KEY_ID|ACCESS_KEY|CREDENTIALS|PROFILE|ACCOUNT|PROJECT|PAT|ARN|RESOURCE_NAME)$")]
    private static partial Regex CredentialShapedName();

    /// <summary>
    /// Resolves the env var name <paramref name="apiKey"/> should be exported under, validating its
    /// shape: <paramref name="apiKeyEnv"/> is the caller's <c>--agent-api-key-env</c>, and
    /// <c>null</c>/blank picks a default for <paramref name="agent"/>. Throws
    /// <see cref="InvalidInputException"/> when neither yields a usable name.
    ///
    /// A name is only resolved once there is a key to export under it, so no key means no name and
    /// no complaint about one — e.g. opencode's free default model needs neither. That rule lives
    /// here rather than at each call site so the CLI and anything else building an
    /// <see cref="AgentConfig"/> can't disagree about when the name is required.
    /// </summary>
    internal static string? ResolveEnvName(AgentKind agent, string? apiKey, string? apiKeyEnv)
    {
        if (apiKey is null)
            return null;

        if (string.IsNullOrWhiteSpace(apiKeyEnv))
            return DefaultEnvName(agent);

        return Validate(apiKeyEnv.Trim());
    }

    /// <summary>claude and opencode expect different credentials by default; pi is multi-provider
    /// with no single default credential, unlike opencode's own free-model provider - the caller
    /// must say which env var to use. Every kind is listed explicitly rather than one of them
    /// serving as the fallback, so a new agent has to state its own default here instead of
    /// silently inheriting opencode's.</summary>
    private static string DefaultEnvName(AgentKind agent) => agent switch
    {
        AgentKind.Claude => "ANTHROPIC_API_KEY",
        AgentKind.OpenCode => "OPENCODE_API_KEY",
        AgentKind.Pi => throw new InvalidInputException("pi has no default credential env var, so one must be given whenever an agent api key is set"),
        _ => throw new NotSupportedException($"No default credential env var for agent: {agent}"),
    };

    private static string Validate(string envName)
    {
        if (!CredentialShapedName().IsMatch(envName))
            throw new InvalidInputException($"'{envName}' must be a credential-shaped environment variable name, e.g. *_API_KEY or *_TOKEN, and not one of rix's own runtime variables");
        return envName;
    }
}
