using Rix.Agents;
using Rix.Job;

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
    public void ParseTranscriptLine_RendersToolArguments_FromStateInput()
    {
        const string line = """{"type":"tool_use","part":{"type":"tool","tool":"bash","callID":"call_01","state":{"status":"completed","input":{"command":"git status","timeout":10000}}}}""";

        Assert.AreEqual("→ bash({\"command\":\"git status\",\"timeout\":10000})", new OpenCodeAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    public void ParseTranscriptLine_RendersBareToolName_ForEmptyInput()
    {
        const string line = """{"type":"tool_use","part":{"type":"tool","tool":"read","callID":"call_01","state":{"status":"completed","input":{}}}}""";

        Assert.AreEqual("→ read(...)", new OpenCodeAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    public void ParseTranscriptLine_TruncatesOversizedToolArguments()
    {
        const string lineTemplate = """{"type":"tool_use","part":{"type":"tool","tool":"edit","callID":"call_01","state":{"status":"completed","input":{"filePath":"a.cs","content":"__CONTENT__"}}}}""";
        var input = string.Join("", Enumerable.Repeat("x", TranscriptLine.MaxToolInputLength * 2));
        var line = lineTemplate.Replace("__CONTENT__", input);

        var transcript = new OpenCodeAgent().ParseTranscriptLine(line);

        Assert.IsNotNull(transcript);
        Assert.IsTrue(transcript.StartsWith("→ edit("));
        Assert.IsTrue(transcript.EndsWith("…)"));
        Assert.IsFalse(transcript.Contains(input), "the oversized argument must be truncated, not inlined whole");
    }

    [TestMethod]
    public void ParseTranscriptLine_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"text","part":{"text":"hi"}}""";

        Assert.AreEqual("hi", new OpenCodeAgent().ParseTranscriptLine(line));
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
}
