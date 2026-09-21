using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class AgentKindParserTests
{
    [TestMethod]
    public void Parse_IsCaseInsensitiveAndTrims()
    {
        Assert.AreEqual(AgentKind.Claude, AgentKindParser.Parse(" Claude "));
        Assert.AreEqual(AgentKind.OpenCode, AgentKindParser.Parse("OPENCODE"));
        Assert.AreEqual(AgentKind.Pi, AgentKindParser.Parse(" PI "));
    }

    [TestMethod]
    public void Parse_Throws_ForUnknownAgent()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => AgentKindParser.Parse("devin"));
        StringAssert.Contains(ex.Message, "devin");
    }
}
