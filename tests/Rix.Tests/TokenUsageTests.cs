using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class TokenUsageTests
{
    [TestMethod]
    public void Accumulate_AddsInputAndOutputTokens_FromResultLine()
    {
        const string line = """{"type":"result","subtype":"success","total_input_tokens":1000,"total_output_tokens":500}""";

        Assert.AreEqual(1500, TokenUsage.Accumulate(0, line));
    }

    [TestMethod]
    public void Accumulate_AddsToRunningTotal()
    {
        const string line = """{"type":"result","total_input_tokens":200,"total_output_tokens":100}""";

        Assert.AreEqual(1800, TokenUsage.Accumulate(1500, line));
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
    public void Accumulate_ReturnsCurrent_ForNonResultOrMalformedLines(string line)
    {
        Assert.AreEqual(7, TokenUsage.Accumulate(7, line));
    }

    [TestMethod]
    public void Accumulate_TreatsNonIntegerTokenFields_AsZero()
    {
        const string line = """{"type":"result","total_input_tokens":"not-a-number","total_output_tokens":null}""";

        Assert.AreEqual(42, TokenUsage.Accumulate(42, line));
    }

    [TestMethod]
    public void Accumulate_ClampsToIntMaxValue_OnOverflow()
    {
        const string line = """{"type":"result","total_input_tokens":2000000000,"total_output_tokens":2000000000}""";

        Assert.AreEqual(int.MaxValue, TokenUsage.Accumulate(int.MaxValue - 1, line));
    }

    [TestMethod]
    public void Accumulate_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"result","total_input_tokens":10,"total_output_tokens":5}""";

        Assert.AreEqual(15, TokenUsage.Accumulate(0, line));
    }
}
