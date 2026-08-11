using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class OpenCodeAgentTranscriptTests
{
    [TestMethod]
    public void ParseTranscriptLine_ReturnsPartText_ForTextEvent()
    {
        const string line = """{"type":"text","part":{"text":"hi"}}""";

        Assert.AreEqual("hi", new OpenCodeAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    public void ParseTranscriptLine_RendersToolName_ForToolUseEvent()
    {
        const string line = """{"type":"tool_use","part":{"type":"tool","tool":"bash","callID":"call_01"}}""";

        Assert.AreEqual("→ bash(...)", new OpenCodeAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    [DataRow("not json at all")]
    [DataRow("""{"type":"step_start","part":{"type":"step-start"}}""")]
    [DataRow("""{"type":"step_finish","part":{"cost":0.5}}""")]
    [DataRow("""{"type":"text","part":{}}""")]
    [DataRow("""{"type":"tool_use","part":{"type":"tool"}}""")]
    [DataRow("{invalid json}")]
    [DataRow("""{"type":123}""")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("42")]
    [DataRow("")]
    public void ParseTranscriptLine_ReturnsNull_ForNonContentOrMalformedLines(string line)
    {
        Assert.IsNull(new OpenCodeAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    public void ParseTranscriptLine_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"text","part":{"text":"hi"}}""";

        Assert.AreEqual("hi", new OpenCodeAgent().ParseTranscriptLine(line));
    }
}
