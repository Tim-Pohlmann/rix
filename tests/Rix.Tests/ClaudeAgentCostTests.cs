using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class ClaudeAgentCostTests
{
    [TestMethod]
    public void ParseCost_ReadsTotalCostUsd()
    {
        const string line = """{"type":"result","subtype":"success","total_cost_usd":0.028521,"usage":{"input_tokens":2036,"output_tokens":14}}""";

        Assert.AreEqual(0.028521m, new ClaudeAgent().ParseCost(line));
    }

    [TestMethod]
    [DataRow("not json at all")]
    [DataRow("""{"type":"assistant","message":"hello"}""")]
    [DataRow("{invalid json}")]
    [DataRow("""{"type":123}""")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("42")]
    [DataRow("")]
    public void ParseCost_ReturnsNull_ForNonResultOrMalformedLines(string line)
    {
        Assert.IsNull(new ClaudeAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_ReturnsNull_WhenResultHasNoCost()
    {
        const string line = """{"type":"result","subtype":"success","usage":{"input_tokens":2036}}""";

        Assert.IsNull(new ClaudeAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_ReturnsNull_WhenCostIsNonNumeric()
    {
        const string line = """{"type":"result","total_cost_usd":"not-a-number"}""";

        Assert.IsNull(new ClaudeAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"result","total_cost_usd":1.5}""";

        Assert.AreEqual(1.5m, new ClaudeAgent().ParseCost(line));
    }
}
