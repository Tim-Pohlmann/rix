using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class AgentKindParserTests
{
    [TestMethod]
    public void Parse_DefaultsToClaude_WhenNullOrWhitespace()
    {
        Assert.AreEqual(AgentKind.Claude, AgentKindParser.Parse(null));
        Assert.AreEqual(AgentKind.Claude, AgentKindParser.Parse("   "));
    }

    [TestMethod]
    public void Parse_IsCaseInsensitiveAndTrims()
    {
        Assert.AreEqual(AgentKind.Claude, AgentKindParser.Parse(" Claude "));
        Assert.AreEqual(AgentKind.OpenCode, AgentKindParser.Parse("OPENCODE"));
    }

    [TestMethod]
    public void Parse_Throws_OnUnknownAgent()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => AgentKindParser.Parse("devin"));
        StringAssert.Contains(ex.Message, "devin");
    }

    [TestMethod]
    public void TryParse_Succeeds_ForKnownAndEmptyValues()
    {
        Assert.IsTrue(AgentKindParser.TryParse(null, out var def, out var defErr));
        Assert.AreEqual(AgentKind.Claude, def);
        Assert.IsNull(defErr);

        Assert.IsTrue(AgentKindParser.TryParse("opencode", out var oc, out _));
        Assert.AreEqual(AgentKind.OpenCode, oc);
    }

    [TestMethod]
    public void TryParse_Fails_WithError_ForUnknownAgent()
    {
        Assert.IsFalse(AgentKindParser.TryParse("devin", out var kind, out var error));
        Assert.AreEqual(AgentKind.Claude, kind);
        StringAssert.Contains(error, "devin");
    }
}
