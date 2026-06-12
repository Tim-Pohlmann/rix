using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class OpenCodeCostTests
{
    [TestMethod]
    public void FromEventLine_ReadsCost_FromStepFinishPart()
    {
        var line = """{"type":"step_finish","sessionID":"s","part":{"cost":0.0123,"tokens":{"input":10,"output":20}}}""";
        Assert.AreEqual(0.0123m, OpenCodeCost.FromEventLine(line));
    }

    [TestMethod]
    public void FromEventLine_ReadsCost_FromTopLevelFallback()
    {
        Assert.AreEqual(1.5m, OpenCodeCost.FromEventLine("""{"type":"message","cost":1.5}"""));
    }

    [TestMethod]
    public void FromEventLine_PrefersPartCost_OverTopLevel()
    {
        var line = """{"cost":9.9,"part":{"cost":0.25}}""";
        Assert.AreEqual(0.25m, OpenCodeCost.FromEventLine(line));
    }

    [TestMethod]
    public void FromEventLine_ReturnsNull_WhenNoCostField()
    {
        Assert.IsNull(OpenCodeCost.FromEventLine("""{"type":"text","part":{"text":"hi"}}"""));
    }

    [TestMethod]
    public void FromEventLine_ReturnsNull_WhenCostIsNotNumeric()
    {
        Assert.IsNull(OpenCodeCost.FromEventLine("""{"part":{"cost":"free"}}"""));
    }

    [TestMethod]
    public void FromEventLine_ReturnsNull_ForNonObjectOrMalformedLines()
    {
        Assert.IsNull(OpenCodeCost.FromEventLine(""));
        Assert.IsNull(OpenCodeCost.FromEventLine("not json"));
        Assert.IsNull(OpenCodeCost.FromEventLine("""["cost"]"""));
        Assert.IsNull(OpenCodeCost.FromEventLine("""{"cost":0.5"""));
    }
}
