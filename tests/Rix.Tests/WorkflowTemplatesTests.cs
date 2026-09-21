using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class WorkflowTemplatesTests
{
    private static readonly string[] ExpectedRelativePaths =
        [".github/workflows/rix-on-ci-failure.yml", ".github/workflows/rix.yml"];

    /// <summary>Looks a template up by the path it lands on rather than by position, so a template
    /// added to <c>Cli/Templates/</c> shifts the discovered order without breaking every assertion
    /// about the two that were already there. Resolved against <see cref="Ref"/>, because what the
    /// assertions are about is the text a caller is actually handed.</summary>
    private static string ContentOf(string fileName)
    => WorkflowTemplates.For(Ref).Single(t => t.RelativePath == $".github/workflows/{fileName}").Content;

    private static readonly WorkflowRef Ref = new("v9");

    [TestMethod]
    public void For_ResolvesBothEmbeddedTemplates_Nonempty()
    {
        var templates = WorkflowTemplates.For(Ref);
        CollectionAssert.AreEqual(ExpectedRelativePaths, templates.Select(t => t.RelativePath).ToArray());
        Assert.IsTrue(templates.All(t => t.Content.Contains("jobs:") && t.Content.Contains("uses:")));
    }

    [TestMethod]
    public void For_RixCaller_TargetsJobReusableWorkflow_AtTheGivenRef()
    {
        var content = ContentOf("rix.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/job.yml@v9");
        StringAssert.Contains(content, "workflow_dispatch:");
    }

    [TestMethod]
    public void For_CiFailureCaller_TargetsOnCiFailureReusableWorkflow_AtTheGivenRef()
    {
        var content = ContentOf("rix-on-ci-failure.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@v9");
        StringAssert.Contains(content, "workflow_run:");
    }

    [TestMethod]
    public void For_LeavesTheEmbeddedTemplatesIntact_SoEachCallSeesItsOwnRef()
    {
        // The substitution is per call, not applied once to the cached copy: two inits in the same
        // process with different refs must not have the first one's ref baked into the second's.
        Assert.AreEqual(WorkflowTemplates.For(new("v1"))[0].Content.Replace("@v1", "@v2", StringComparison.Ordinal), WorkflowTemplates.For(new("v2"))[0].Content);
    }
}
