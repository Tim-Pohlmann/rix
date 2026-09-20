using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class AgentCredentialTests
{
    /// <summary>Stands in for any <c>--agent-api-key</c>: the resolver only cares whether a key is
    /// there, never what it says, so every case that is about the name passes the same one.</summary>
    private const string ApiKey = "sk-test";

    [TestMethod]
    public void ResolveEnvName_DefaultsToAnthropicApiKey_ForClaude()
    => Assert.AreEqual("ANTHROPIC_API_KEY", AgentCredential.ResolveEnvName(AgentKind.Claude, ApiKey, null));

    [TestMethod]
    public void ResolveEnvName_DefaultsToOpenCodeApiKey_ForOpenCode()
    => Assert.AreEqual("OPENCODE_API_KEY", AgentCredential.ResolveEnvName(AgentKind.OpenCode, ApiKey, null));

    [TestMethod]
    public void ResolveEnvName_RequiresExplicitEnv_ForPi()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => AgentCredential.ResolveEnvName(AgentKind.Pi, ApiKey, null));

        StringAssert.Contains(ex.Message, "pi");
    }

    [TestMethod]
    public void ResolveEnvName_TrimsAndUsesExplicitOverride()
    => Assert.AreEqual("OPENAI_API_KEY", AgentCredential.ResolveEnvName(AgentKind.OpenCode, ApiKey, " OPENAI_API_KEY "));

    [TestMethod]
    public void ResolveEnvName_UsesExplicitOverride_ForPi()
    => Assert.AreEqual("OPENAI_API_KEY", AgentCredential.ResolveEnvName(AgentKind.Pi, ApiKey, "OPENAI_API_KEY"));

    [TestMethod]
    [DataRow("AWS_ACCESS_KEY_ID")]
    [DataRow("GOOGLE_APPLICATION_CREDENTIALS")]
    [DataRow("SNOWFLAKE_CORTEX_TOKEN")]
    [DataRow("AZURE_CLIENT_ID_KEY_ID")]
    public void ResolveEnvName_AcceptsCredentialShapedNames(string envName)
    => Assert.AreEqual(envName, AgentCredential.ResolveEnvName(AgentKind.OpenCode, ApiKey, envName));

    [TestMethod]
    public void ResolveEnvName_RejectsNameWithoutCredentialShapedSuffix()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => AgentCredential.ResolveEnvName(AgentKind.OpenCode, ApiKey, "MY_SECRET"));

        StringAssert.Contains(ex.Message, "MY_SECRET");
    }

    [TestMethod]
    [DataRow("RIX_AGENT")]
    [DataRow("AGENT_API_KEY_EXTRA")]
    [DataRow("GITHUB_TOKEN")]
    public void ResolveEnvName_RejectsRixAndGitHubRuntimeVariables(string envName)
    => Assert.ThrowsExactly<InvalidInputException>(() => AgentCredential.ResolveEnvName(AgentKind.OpenCode, ApiKey, envName));

    [TestMethod]
    public void ResolveEnvName_TreatsBlankOverride_AsOmitted()
    => Assert.AreEqual("ANTHROPIC_API_KEY", AgentCredential.ResolveEnvName(AgentKind.Claude, ApiKey, "   "));

    /// <summary>pi is the agent with no default env var, so resolving to null here rather than
    /// throwing shows the missing-key check runs before the default is looked up at all.</summary>
    [TestMethod]
    public void ResolveEnvName_ResolvesNoName_WhenThereIsNoKeyToExport()
    => Assert.IsNull(AgentCredential.ResolveEnvName(AgentKind.Pi, null, null));

    /// <summary>The name that <see cref="ResolveEnvName_RejectsNameWithoutCredentialShapedSuffix"/>
    /// rejects, accepted in silence once there is no key: without a key the name is never exported,
    /// so there is nothing to complain about.</summary>
    [TestMethod]
    public void ResolveEnvName_SkipsValidatingTheName_WhenThereIsNoKeyToExport()
    => Assert.IsNull(AgentCredential.ResolveEnvName(AgentKind.OpenCode, null, "MY_SECRET"));
}
