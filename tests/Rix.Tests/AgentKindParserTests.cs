using Rix.Agents;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class AgentKindParserTests
{
    [TestMethod]
    public void Parse_DefaultsToClaude_WhenNullOrWhitespace()
    {
        Assert.AreEqual(AgentKind.Claude, AgentKindParser.Parse(null));
        Assert.AreEqual(AgentKind.Claude, AgentKindParser.Parse("   "));
    }

    [TestMethod]
    public void Parse_IsCaseInsensitiveAndTrims()
    {
        Assert.AreEqual(AgentKind.Claude, AgentKindParser.Parse(" Claude "));
        Assert.AreEqual(AgentKind.OpenCode, AgentKindParser.Parse("OPENCODE"));
    }

    [TestMethod]
    public void Parse_Throws_OnUnknownAgent()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => AgentKindParser.Parse("devin"));
        StringAssert.Contains(ex.Message, "devin");
    }

    [TestMethod]
    public void FromInputs_SelectsOpenCode_FromAgentArgument()
    {
        var config = JobConfig.FromInputs("owner/repo", "do it", "tok",
            maxTokens: null, timeoutMinutes: null, workDir: Path.GetTempPath(),
            outputDir: Path.GetTempPath(), agent: "opencode");

        Assert.AreEqual(AgentKind.OpenCode, config.Agent);
    }

    [TestMethod]
    public void FromInputs_DefaultsToClaude_WhenAgentOmitted()
    {
        var config = JobConfig.FromInputs("owner/repo", "do it", "tok",
            maxTokens: null, timeoutMinutes: null, workDir: Path.GetTempPath(),
            outputDir: Path.GetTempPath());

        Assert.AreEqual(AgentKind.Claude, config.Agent);
    }
}
