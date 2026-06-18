using Rix.Agents;

namespace Rix.Tests;

[TestClass]
public class OpenCodeAgentCostTests
{
    [TestMethod]
    public void ParseCost_ReadsCost_FromStepFinishPart()
    {
        var line = """{"type":"step_finish","sessionID":"s","part":{"cost":0.0123,"tokens":{"input":10,"output":20}}}""";
        Assert.AreEqual(0.0123m, new OpenCodeAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_ReadsCost_FromTopLevelFallback()
    {
        Assert.AreEqual(1.5m, new OpenCodeAgent().ParseCost("""{"type":"message","cost":1.5}"""));
    }

    [TestMethod]
    public void ParseCost_PrefersPartCost_OverTopLevel()
    {
        var line = """{"cost":9.9,"part":{"cost":0.25}}""";
        Assert.AreEqual(0.25m, new OpenCodeAgent().ParseCost(line));
    }

    [TestMethod]
    public void ParseCost_ReturnsNull_WhenNoCostField()
    {
        Assert.IsNull(new OpenCodeAgent().ParseCost("""{"type":"text","part":{"text":"hi"}}"""));
    }

    [TestMethod]
    public void ParseCost_ReturnsNull_WhenCostIsNotNumeric()
    {
        Assert.IsNull(new OpenCodeAgent().ParseCost("""{"part":{"cost":"free"}}"""));
    }

    [TestMethod]
    public void ParseCost_ReturnsNull_ForNonObjectOrMalformedLines()
    {
        Assert.IsNull(new OpenCodeAgent().ParseCost(""));
        Assert.IsNull(new OpenCodeAgent().ParseCost("not json"));
        Assert.IsNull(new OpenCodeAgent().ParseCost("""["cost"]"""));
        Assert.IsNull(new OpenCodeAgent().ParseCost("""{"cost":0.5"""));
    }
}
