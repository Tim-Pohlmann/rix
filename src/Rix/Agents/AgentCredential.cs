using System.Text.RegularExpressions;

namespace Rix.Agents;

/// <summary>
/// Resolves the environment variable name an <c>--agent-api-key</c> should be exported as for the
/// child agent CLI process, and validates it. Used by <see cref="Job.JobConfig.Create"/> so the
/// resolved (name, value) pair can be attached to the run's <see cref="AgentInvocation.EnvironmentOverrides"/>
/// — the child process gets the credential without it ever needing to exist under that name in
/// rix's own process environment.
/// </summary>
internal static partial class AgentCredential
{
    /// <summary>
    /// Restricts env var names to credential-shaped suffixes covering opencode's supported
    /// providers (ANTHROPIC_API_KEY, AWS_ACCESS_KEY_ID, GOOGLE_APPLICATION_CREDENTIALS,
    /// SNOWFLAKE_CORTEX_TOKEN, ...), rather than deny-listing every internal/runtime variable a
    /// caller could otherwise clobber.
    /// </summary>
    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*_(API_KEY|TOKEN|KEY_ID|ACCESS_KEY|CREDENTIALS|PROFILE|ACCOUNT|PROJECT|PAT|ARN|RESOURCE_NAME)$")]
    private static partial Regex CredentialShapedName();

    /// <summary>
    /// Resolves the env var name <paramref name="apiKeyEnv"/> (the caller's <c>--agent-api-key-env</c>,
    /// or <c>null</c>/blank to pick a default for <paramref name="agent"/>) and validates its shape.
    /// Only called when an api key is actually present — <see cref="Job.JobConfig.Create"/> skips
    /// this entirely otherwise, since e.g. opencode's free default model needs no key.
    /// </summary>
    internal static ParseResult<string> ResolveEnvName(AgentKind agent, string? apiKeyEnv)
    {
        if (string.IsNullOrWhiteSpace(apiKeyEnv))
            return DefaultEnvName(agent).Match(onSuccess: Validate, onError: error => new ParseError<string>(error));

        return Validate(apiKeyEnv.Trim());
    }

    /// <summary>claude and opencode expect different credentials by default; pi is multi-provider
    /// with no single default credential, unlike opencode's own free-model provider - the caller
    /// must say which env var to use.</summary>
    private static ParseResult<string> DefaultEnvName(AgentKind agent) => agent switch
    {
        AgentKind.Claude => new ParseSuccess<string>("ANTHROPIC_API_KEY"),
        AgentKind.Pi => new ParseError<string>("is required when agent=pi and agent-api-key is set"),
        _ => new ParseSuccess<string>("OPENCODE_API_KEY"),
    };

    /// <summary>
    /// A couple of the allowed suffixes (e.g. _TOKEN) would otherwise overlap with vars rix's own
    /// plumbing relies on, so also block RIX_*/AGENT_API_KEY* (rix's own runtime vars) and GITHUB_*
    /// (GITHUB_TOKEN etc) by name.
    /// </summary>
    private static ParseResult<string> Validate(string envName)
    {
        var blocked =
            envName.StartsWith("RIX_", StringComparison.Ordinal) ||
            envName.StartsWith("AGENT_API_KEY", StringComparison.Ordinal) ||
            envName.StartsWith("GITHUB_", StringComparison.Ordinal) ||
            !CredentialShapedName().IsMatch(envName);

        return blocked switch
        {
            true => new ParseError<string>($"'{envName}' must be a credential-shaped environment variable name, e.g. *_API_KEY or *_TOKEN, and not one of rix's own runtime variables"),
            false => new ParseSuccess<string>(envName),
        };
    }
}
