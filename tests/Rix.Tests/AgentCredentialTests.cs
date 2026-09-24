using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class AgentCredentialTests
{
    /// <summary>Stands in for any <c>--agent-api-key</c>: resolution only cares whether a key is
    /// there, never what it says, so every case that is about the name passes the same one.</summary>
    private const string ApiKey = "sk-test";

    private static string? EnvNameOf(AgentKind agent, string? apiKeyEnv)
    => AgentCredential.Resolve(agent, ApiKey, apiKeyEnv)?.EnvName;

    [TestMethod]
    public void Resolve_DefaultsToAnthropicApiKey_ForClaude()
    => Assert.AreEqual("ANTHROPIC_API_KEY", EnvNameOf(AgentKind.Claude, null));

    [TestMethod]
    public void Resolve_DefaultsToOpenCodeApiKey_ForOpenCode()
    => Assert.AreEqual("OPENCODE_API_KEY", EnvNameOf(AgentKind.OpenCode, null));

    /// <summary>Asserts the whole message, not a substring of it. "pi" appears in almost any
    /// wording this could take, so a Contains check passes just as happily for a message that has
    /// quietly been rephrased into something the flag prefix no longer reads well with.</summary>
    [TestMethod]
    public void Resolve_RequiresExplicitEnv_ForPi()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => EnvNameOf(AgentKind.Pi, null));

        Assert.AreEqual("is required when agent=pi and agent-api-key is set", ex.Message);
    }

    [TestMethod]
    public void Resolve_TrimsAndUsesExplicitOverride()
    => Assert.AreEqual("OPENAI_API_KEY", EnvNameOf(AgentKind.OpenCode, " OPENAI_API_KEY "));

    [TestMethod]
    public void Resolve_UsesExplicitOverride_ForPi()
    => Assert.AreEqual("OPENAI_API_KEY", EnvNameOf(AgentKind.Pi, "OPENAI_API_KEY"));

    [TestMethod]
    [DataRow("AWS_ACCESS_KEY_ID")]
    [DataRow("GOOGLE_APPLICATION_CREDENTIALS")]
    [DataRow("SNOWFLAKE_CORTEX_TOKEN")]
    [DataRow("AZURE_CLIENT_ID_KEY_ID")]
    public void Resolve_AcceptsCredentialShapedNames(string envName)
    => Assert.AreEqual(envName, EnvNameOf(AgentKind.OpenCode, envName));

    [TestMethod]
    public void Resolve_RejectsNameWithoutCredentialShapedSuffix()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => EnvNameOf(AgentKind.OpenCode, "MY_SECRET"));

        StringAssert.Contains(ex.Message, "MY_SECRET");
    }

    [TestMethod]
    [DataRow("RIX_AGENT")]
    [DataRow("AGENT_API_KEY_EXTRA")]
    [DataRow("GITHUB_TOKEN")]
    public void Resolve_RejectsRixAndGitHubRuntimeVariables(string envName)
    => Assert.ThrowsExactly<InvalidInputException>(() => EnvNameOf(AgentKind.OpenCode, envName));

    [TestMethod]
    public void Resolve_TreatsBlankOverride_AsOmitted()
    => Assert.AreEqual("ANTHROPIC_API_KEY", EnvNameOf(AgentKind.Claude, "   "));

    [TestMethod]
    public void Resolve_KeepsTheKeyItWasGiven_AlongsideTheName()
    => Assert.AreEqual(new AgentCredential("ANTHROPIC_API_KEY", ApiKey), AgentCredential.Resolve(AgentKind.Claude, ApiKey, null));

    /// <summary>pi is the agent with no default env var, so resolving to null here rather than
    /// throwing shows the missing-key check runs before the default is looked up at all.</summary>
    [TestMethod]
    public void Resolve_ResolvesNoCredential_WhenThereIsNoKeyToExport()
    => Assert.IsNull(AgentCredential.Resolve(AgentKind.Pi, null, null));

    /// <summary>The name that <see cref="Resolve_RejectsNameWithoutCredentialShapedSuffix"/>
    /// rejects, accepted in silence once there is no key: without a key the name is never exported,
    /// so there is nothing to complain about.</summary>
    [TestMethod]
    public void Resolve_SkipsValidatingTheName_WhenThereIsNoKeyToExport()
    => Assert.IsNull(AgentCredential.Resolve(AgentKind.OpenCode, null, "MY_SECRET"));

    /// <summary>A record's generated ToString prints every member, so the one that carries a secret
    /// has to override it - otherwise interpolating a config into a log line leaks the key.</summary>
    [TestMethod]
    public void ToString_RedactsTheKey_AndKeepsTheName()
    {
        var rendered = $"{new AgentCredential("ANTHROPIC_API_KEY", "sk-super-secret")}";

        StringAssert.Contains(rendered, "ANTHROPIC_API_KEY");
        Assert.IsFalse(rendered.Contains("sk-super-secret", StringComparison.Ordinal), rendered);
    }
}
