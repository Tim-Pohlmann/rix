using System.Text.RegularExpressions;

namespace Rix.Agents;

/// <summary>
/// The credential for the child agent CLI process: the raw <c>--agent-api-key</c> and the
/// environment variable name it should be exported under, kept together because neither is any use
/// without the other. Resolved by the CLI when building a <see cref="Job.JobConfig"/> and attached
/// to the run's <see cref="AgentInvocation.EnvironmentOverrides"/> — the child process gets the
/// credential without it ever needing to exist under that name in rix's own process environment.
///
/// One nullable value rather than two: a name without a key exports nothing, and a key without a
/// name has nowhere to go, so the pair is the unit that is either wholly present or wholly absent.
/// Holding them as two <c>string?</c>s made that a rule the consumer had to restate — and could
/// only restate with a null-forgiving <c>!</c>, since the type said the invalid halves were
/// reachable.
/// </summary>
internal sealed partial record AgentCredential(string EnvName, string Key)
{
    /// <summary>
    /// Resolves the env var name <paramref name="apiKey"/> should be exported under, validating its
    /// shape: <paramref name="apiKeyEnv"/> is the caller's <c>--agent-api-key-env</c>, and
    /// <c>null</c>/blank picks a default for <paramref name="agent"/>. Throws
    /// <see cref="InvalidInputException"/> when neither yields a usable name.
    ///
    /// A name is only resolved once there is a key to export under it, so no key means no
    /// credential and no complaint about the name — e.g. opencode's free default model needs
    /// neither. That rule lives here rather than at each call site so the CLI and anything else
    /// building an <see cref="AgentConfig"/> can't disagree about when the name is required.
    /// </summary>
    internal static AgentCredential? Resolve(AgentKind agent, string? apiKey, string? apiKeyEnv)
    {
        if (apiKey is null)
            return null;

        if (string.IsNullOrWhiteSpace(apiKeyEnv))
            return new AgentCredential(DefaultEnvName(agent), apiKey);

        return new AgentCredential(Validate(apiKeyEnv.Trim()), apiKey);
    }

    /// <summary>Redacts <see cref="Key"/>, which a record's generated <c>ToString</c> would
    /// otherwise print in full wherever a config carrying this is interpolated into a log line.
    /// The name is not secret and is the half worth seeing when diagnosing a wrong-provider run.</summary>
    public override string ToString() => $"{nameof(AgentCredential)} {{ {nameof(EnvName)} = {EnvName}, {nameof(Key)} = <redacted> }}";

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
