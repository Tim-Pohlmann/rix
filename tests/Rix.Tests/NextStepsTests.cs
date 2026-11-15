using Rix.Initialize;
using System.Text.RegularExpressions;

namespace Rix.Tests;

[TestClass]
public partial class NextStepsTests
{
    [TestMethod]
    public void For_PointsAFactoryAtTheReposItServes()
    {
        var steps = NextSteps.For(new FactorySetup(new("v0")));

        StringAssert.Contains(steps, "on every repo this factory serves");
        StringAssert.Contains(steps, "In each repo this factory serves");
        // A factory answers dispatched failures, so there is no watched workflow to rename.
        Assert.IsFalse(steps.Contains("workflows: [\"CI\"]", StringComparison.Ordinal));
    }

    /// <summary>The steps are prose about the templates, so nothing else would notice a template
    /// renamed under them.</summary>
    [TestMethod]
    public void For_NamesOnlyWorkflowsTheSetupWrites()
    {
        IInitializeSetup[] setups = [new StandaloneSetup(new("v0")), new FactorySetup(new("v0"))];

        foreach (var setup in setups)
        {
            var written = WorkflowTemplates.Of(setup).Select(t => Path.GetFileName(t.RelativePath)).ToArray();
            foreach (var named in WorkflowFileName().Matches(NextSteps.For(setup)).Select(m => m.Value))
                CollectionAssert.Contains(written, named, $"{setup.GetType().Name}'s next steps name {named}");
        }
    }

    [GeneratedRegex(@"[a-z][a-z-]*\.yml")]
    private static partial Regex WorkflowFileName();
}
