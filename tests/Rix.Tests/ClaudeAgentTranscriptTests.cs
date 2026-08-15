using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class ClaudeAgentTranscriptTests
{
    [TestMethod]
    public void ParseTranscriptLine_RendersTextAndToolUseBlocks_FromAssistantMessage()
    {
        const string line = """
            {"type":"assistant","message":{"id":"msg_01","role":"assistant","content":[
                {"type":"text","text":"I will fix the bug."},
                {"type":"tool_use","id":"toolu_01","name":"Bash","input":{"command":"dotnet test"}}
            ]}}
            """;

        var transcript = new ClaudeAgent().ParseTranscriptLine(line);

        Assert.IsNotNull(transcript);
        StringAssert.Contains(transcript, "I will fix the bug.");
        StringAssert.Contains(transcript, "→ Bash({\"command\":\"dotnet test\"})");
    }

    [TestMethod]
    public void ParseTranscriptLine_JoinsBlocks_WithNewlines()
    {
        const string line = """{"type":"assistant","message":{"content":[{"type":"text","text":"first"},{"type":"text","text":"second"}]}}""";

        Assert.AreEqual("first\nsecond", new ClaudeAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    public void ParseTranscriptLine_SkipsToolResultAndNonTextBlocks()
    {
        const string line = """
            {"type":"assistant","message":{"content":[
                {"type":"tool_use","name":"Read","input":{"file_path":"a.cs"}},
                {"type":"text","text":"done"}
            ]}}
            """;

        var transcript = new ClaudeAgent().ParseTranscriptLine(line);

        Assert.IsNotNull(transcript);
        StringAssert.Contains(transcript, "→ Read({\"file_path\":\"a.cs\"})");
        StringAssert.Contains(transcript, "done");
    }

    [TestMethod]
    [DataRow("not json at all")]
    [DataRow("""{"type":"result","subtype":"success","total_cost_usd":0.5}""")]
    [DataRow("""{"type":"system","subtype":"init","message":"hi"}""")]
    [DataRow("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","content":"ls"}]}}""")]
    [DataRow("{invalid json}")]
    [DataRow("""{"type":"assistant","message":{}}""")]
    [DataRow("""{"type":"assistant","message":{"content":[]}}""")]
    [DataRow("""{"type":"assistant","message":{"content":[{"type":"image","source":{"type":"base64"}}]}}""")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("42")]
    [DataRow("")]
    public void ParseTranscriptLine_ReturnsNull_ForNonAssistantOrMalformedLines(string line)
    {
        Assert.IsNull(new ClaudeAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    public void ParseTranscriptLine_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"assistant","message":{"content":[{"type":"text","text":"hi"}]}}""";

        Assert.AreEqual("hi", new ClaudeAgent().ParseTranscriptLine(line));
    }
}
