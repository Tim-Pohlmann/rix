using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class AgentCredentialTests
{
    [TestMethod]
    public void ResolveEnvName_DefaultsToAnthropicApiKey_ForClaude()
    => Assert.AreEqual("ANTHROPIC_API_KEY", AgentCredential.ResolveEnvName(AgentKind.Claude, null));

    [TestMethod]
    public void ResolveEnvName_DefaultsToOpenCodeApiKey_ForOpenCode()
    => Assert.AreEqual("OPENCODE_API_KEY", AgentCredential.ResolveEnvName(AgentKind.OpenCode, null));

    [TestMethod]
    public void ResolveEnvName_RequiresExplicitEnv_ForPi()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => AgentCredential.ResolveEnvName(AgentKind.Pi, null));

        StringAssert.Contains(ex.Message, "pi");
    }

    [TestMethod]
    public void ResolveEnvName_TrimsAndUsesExplicitOverride()
    => Assert.AreEqual("OPENAI_API_KEY", AgentCredential.ResolveEnvName(AgentKind.OpenCode, " OPENAI_API_KEY "));

    [TestMethod]
    public void ResolveEnvName_UsesExplicitOverride_ForPi()
    => Assert.AreEqual("OPENAI_API_KEY", AgentCredential.ResolveEnvName(AgentKind.Pi, "OPENAI_API_KEY"));

    [TestMethod]
    [DataRow("AWS_ACCESS_KEY_ID")]
    [DataRow("GOOGLE_APPLICATION_CREDENTIALS")]
    [DataRow("SNOWFLAKE_CORTEX_TOKEN")]
    [DataRow("AZURE_CLIENT_ID_KEY_ID")]
    public void ResolveEnvName_AcceptsCredentialShapedNames(string envName)
    => Assert.AreEqual(envName, AgentCredential.ResolveEnvName(AgentKind.OpenCode, envName));

    [TestMethod]
    public void ResolveEnvName_RejectsNameWithoutCredentialShapedSuffix()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => AgentCredential.ResolveEnvName(AgentKind.OpenCode, "MY_SECRET"));

        StringAssert.Contains(ex.Message, "MY_SECRET");
    }

    [TestMethod]
    [DataRow("RIX_AGENT")]
    [DataRow("AGENT_API_KEY_EXTRA")]
    [DataRow("GITHUB_TOKEN")]
    public void ResolveEnvName_RejectsRixAndGitHubRuntimeVariables(string envName)
    => Assert.ThrowsExactly<InvalidInputException>(() => AgentCredential.ResolveEnvName(AgentKind.OpenCode, envName));

    [TestMethod]
    public void ResolveEnvName_TreatsBlankOverride_AsOmitted()
    => Assert.AreEqual("ANTHROPIC_API_KEY", AgentCredential.ResolveEnvName(AgentKind.Claude, "   "));
}
