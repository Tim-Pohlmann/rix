using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class PiAgentCostTests
{
    [TestMethod]
    public void ParseCost_SumsAssistantMessageCosts_FromAgentEnd()
    {
        const string line = """
            {"type":"agent_end","messages":[
                {"role":"user","content":"hi"},
                {"role":"assistant","usage":{"cost":{"input":0.01,"output":0.02,"total":0.03}}},
                {"role":"toolResult","content":"ok"},
                {"role":"assistant","usage":{"cost":{"input":0.04,"output":0.05,"total":0.09}}}
            ]}
            """;

        Assert.AreEqual(0.12m, new PiAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_ReturnsZero_WhenAgentEndHasNoCostedMessages()
    {
        const string line = """{"type":"agent_end","messages":[{"role":"user","content":"hi"}]}""";

        Assert.AreEqual(0m, new PiAgent().ParseCost(line));
    }

    [TestMethod]
    [DataRow("not json at all")]
    [DataRow("""{"type":"message_end","messages":[]}""")]
    [DataRow("{invalid json}")]
    [DataRow("""{"type":123}""")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("42")]
    [DataRow("")]
    public void ParseCost_ReturnsNull_ForNonAgentEndOrMalformedLines(string line)
    {
        Assert.IsNull(new PiAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_ReturnsNull_WhenAgentEndHasNoMessages()
    {
        const string line = """{"type":"agent_end"}""";

        Assert.IsNull(new PiAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_IgnoresMessagesWithNonNumericCost()
    {
        const string line = """
            {"type":"agent_end","messages":[
                {"role":"assistant","usage":{"cost":{"total":"free"}}},
                {"role":"assistant","usage":{"cost":{"total":0.5}}}
            ]}
            """;

        Assert.AreEqual(0.5m, new PiAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"agent_end","messages":[{"role":"assistant","usage":{"cost":{"total":1.5}}}]}""";

        Assert.AreEqual(1.5m, new PiAgent().ParseCost(line));
    }
}
