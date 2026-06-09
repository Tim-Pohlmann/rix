using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobCostTests
{
    [TestMethod]
    public void FromResultLine_ReadsTotalCostUsd()
    {
        const string line = """{"type":"result","subtype":"success","total_cost_usd":0.028521,"usage":{"input_tokens":2036,"output_tokens":14}}""";

        Assert.AreEqual(0.028521m, JobCost.FromResultLine(line));
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
        Assert.IsNull(JobCost.FromResultLine(line));
    }

    [TestMethod]
    public void FromResultLine_ReturnsNull_WhenResultHasNoCost()
    {
        const string line = """{"type":"result","subtype":"success","usage":{"input_tokens":2036}}""";

        Assert.IsNull(JobCost.FromResultLine(line));
    }

    [TestMethod]
    public void FromResultLine_ReturnsNull_WhenCostIsNonNumeric()
    {
        const string line = """{"type":"result","total_cost_usd":"not-a-number"}""";

        Assert.IsNull(JobCost.FromResultLine(line));
    }

    [TestMethod]
    public void FromResultLine_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"result","total_cost_usd":1.5}""";

        Assert.AreEqual(1.5m, JobCost.FromResultLine(line));
    }
}
