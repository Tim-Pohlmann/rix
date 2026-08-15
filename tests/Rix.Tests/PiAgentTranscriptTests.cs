using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class PiAgentTranscriptTests
{
    [TestMethod]
    public void ParseTranscriptLine_RendersAssistantMessages_FromAgentEnd()
    {
        const string line = """
            {"type":"agent_end","messages":[
                {"role":"user","content":[{"type":"text","text":"hi"}]},
                {"role":"assistant","content":[{"type":"text","text":"Let me check."},{"type":"toolCall","name":"read","arguments":{"file_path":"a.cs"}}]},
                {"role":"toolResult","content":[{"type":"text","text":"ok"}]},
                {"role":"assistant","content":[{"type":"text","text":"Done."}]}
            ]}
            """;

        var transcript = new PiAgent().ParseTranscriptLine(line);

        Assert.IsNotNull(transcript);
        StringAssert.Contains(transcript, "Let me check.");
        StringAssert.Contains(transcript, "→ read({\"file_path\":\"a.cs\"})");
        StringAssert.Contains(transcript, "Done.");
        // user and toolResult feedback must not leak into the transcript
        Assert.IsFalse(transcript.Contains("hi"));
        Assert.IsFalse(transcript.Contains("ok"));
    }

    [TestMethod]
    public void ParseTranscriptLine_ReturnsNull_WhenNoAssistantMessages()
    {
        const string line = """
            {"type":"agent_end","messages":[
                {"role":"user","content":[{"type":"text","text":"hi"}]},
                {"role":"toolResult","content":[{"type":"text","text":"ok"}]}
            ]}
            """;

        Assert.IsNull(new PiAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    [DataRow("not json at all")]
    [DataRow("""{"type":"message_end","messages":[]}""")]
    [DataRow("""{"type":"agent_end"}""")]
    [DataRow("{invalid json}")]
    [DataRow("""{"type":123}""")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("42")]
    [DataRow("")]
    public void ParseTranscriptLine_ReturnsNull_ForNonAgentEndOrMalformedLines(string line)
    {
        Assert.IsNull(new PiAgent().ParseTranscriptLine(line));
    }

    [TestMethod]
    public void ParseTranscriptLine_HandlesLeadingWhitespace()
    {
        const string line = """   {"type":"agent_end","messages":[{"role":"assistant","content":[{"type":"text","text":"hi"}]}]}""";

        Assert.AreEqual("hi", new PiAgent().ParseTranscriptLine(line));
    }
}
