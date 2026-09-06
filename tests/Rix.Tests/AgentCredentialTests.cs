using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class AgentCredentialTests
{
    private static string Unwrap(ParseResult<string> result) => result switch
    {
        ParseSuccess<string> s => s.Value,
        ParseError<string> e => throw new AssertFailedException($"expected success, got error: {e.Error}"),
        _ => throw new AssertFailedException("unexpected ParseResult case"),
    };

    private static string UnwrapError(ParseResult<string> result) => result switch
    {
        ParseError<string> e => e.Error,
        var other => throw new AssertFailedException($"expected error, got: {other}"),
    };

    [TestMethod]
    public void ResolveEnvName_DefaultsToAnthropicApiKey_ForClaude()
    => Assert.AreEqual("ANTHROPIC_API_KEY", Unwrap(AgentCredential.ResolveEnvName(AgentKind.Claude, null)));

    [TestMethod]
    public void ResolveEnvName_DefaultsToOpenCodeApiKey_ForOpenCode()
    => Assert.AreEqual("OPENCODE_API_KEY", Unwrap(AgentCredential.ResolveEnvName(AgentKind.OpenCode, null)));

    [TestMethod]
    public void ResolveEnvName_RequiresExplicitEnv_ForPi()
    {
        var error = UnwrapError(AgentCredential.ResolveEnvName(AgentKind.Pi, null));

        StringAssert.Contains(error, "pi");
    }

    [TestMethod]
    public void ResolveEnvName_TrimsAndUsesExplicitOverride()
    => Assert.AreEqual("OPENAI_API_KEY", Unwrap(AgentCredential.ResolveEnvName(AgentKind.OpenCode, " OPENAI_API_KEY ")));

    [TestMethod]
    public void ResolveEnvName_UsesExplicitOverride_ForPi()
    => Assert.AreEqual("OPENAI_API_KEY", Unwrap(AgentCredential.ResolveEnvName(AgentKind.Pi, "OPENAI_API_KEY")));

    [TestMethod]
    [DataRow("AWS_ACCESS_KEY_ID")]
    [DataRow("GOOGLE_APPLICATION_CREDENTIALS")]
    [DataRow("SNOWFLAKE_CORTEX_TOKEN")]
    [DataRow("AZURE_CLIENT_ID_KEY_ID")]
    public void ResolveEnvName_AcceptsCredentialShapedNames(string envName)
    => Assert.AreEqual(envName, Unwrap(AgentCredential.ResolveEnvName(AgentKind.OpenCode, envName)));

    [TestMethod]
    public void ResolveEnvName_RejectsNameWithoutCredentialShapedSuffix()
    {
        var error = UnwrapError(AgentCredential.ResolveEnvName(AgentKind.OpenCode, "MY_SECRET"));

        StringAssert.Contains(error, "MY_SECRET");
    }

    [TestMethod]
    [DataRow("RIX_AGENT")]
    [DataRow("AGENT_API_KEY_EXTRA")]
    [DataRow("GITHUB_TOKEN")]
    public void ResolveEnvName_RejectsRixAndGitHubRuntimeVariables(string envName)
    => Assert.IsInstanceOfType<ParseError<string>>(AgentCredential.ResolveEnvName(AgentKind.OpenCode, envName));

    [TestMethod]
    public void ResolveEnvName_TreatsBlankOverride_AsOmitted()
    => Assert.AreEqual("ANTHROPIC_API_KEY", Unwrap(AgentCredential.ResolveEnvName(AgentKind.Claude, "   ")));
}
