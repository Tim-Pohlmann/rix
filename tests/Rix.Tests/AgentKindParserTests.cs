using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class AgentKindParserTests
{
    private static AgentKind Unwrap(ParseResult<AgentKind> result) => result switch
    {
        ParseSuccess<AgentKind> s => s.Value,
        ParseError<AgentKind> e => throw new AssertFailedException($"expected success, got error: {e.Error}"),
        _ => throw new AssertFailedException("unexpected ParseResult case"),
    };

    [TestMethod]
    public void Parse_IsCaseInsensitiveAndTrims()
    {
        Assert.AreEqual(AgentKind.Claude, Unwrap(AgentKindParser.Parse(" Claude ")));
        Assert.AreEqual(AgentKind.OpenCode, Unwrap(AgentKindParser.Parse("OPENCODE")));
    }

    [TestMethod]
    public void Parse_ReturnsError_ForUnknownAgent()
    {
        var error = AgentKindParser.Parse("devin") switch
        {
            ParseError<AgentKind> e => e.Error,
            var other => throw new AssertFailedException($"expected error, got: {other}"),
        };
        StringAssert.Contains(error, "devin");
    }
}
