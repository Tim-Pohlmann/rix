using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class ClaudeCostTests
{
    [TestMethod]
    public void FromResultLine_ReadsTotalCostUsd()
    {
        const string line = """{"type":"result","subtype":"success","total_cost_usd":0.028521,"usage":{"input_tokens":2036,"output_tokens":14}}""";

        Assert.AreEqual(0.028521m, ClaudeCost.FromResultLine(line));
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
    public void FromResultLine_ReturnsNull_ForNonResultOrMalformedLines(string line)
    {
        Assert.IsNull(ClaudeCost.FromResultLine(line));
    }

    [TestMethod]
    public void FromResultLine_ReturnsNull_WhenResultHasNoCost()
    {
        const string line = """{"type":"result","subtype":"success","usage":{"input_tokens":2036}}""";

        Assert.IsNull(ClaudeCost.FromResultLine(line));
    }

    [TestMethod]
    public void FromResultLine_ReturnsNull_WhenCostIsNonNumeric()
    {
        const string line = """{"type":"result","total_cost_usd":"not-a-number"}""";

        Assert.IsNull(ClaudeCost.FromResultLine(line));
    }

    [TestMethod]
    public void FromResultLine_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"result","total_cost_usd":1.5}""";

        Assert.AreEqual(1.5m, ClaudeCost.FromResultLine(line));
    }
}
