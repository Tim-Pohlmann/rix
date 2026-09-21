using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class WorkflowTemplatesTests
{
    private static readonly string[] ExpectedRelativePaths =
        [".github/workflows/rix-on-ci-failure.yml", ".github/workflows/rix.yml"];

    /// <summary>Looks a template up by the path it lands on rather than by position, so a template
    /// added to <c>Cli/Templates/</c> shifts the discovered order without breaking every assertion
    /// about the two that were already there.</summary>
    private static string ContentOf(string fileName)
    => WorkflowTemplates.All.Single(t => t.RelativePath == $".github/workflows/{fileName}").Content;

    [TestMethod]
    public void All_ResolvesBothEmbeddedTemplates_Nonempty()
    {
        var paths = WorkflowTemplates.All.Select(t => t.RelativePath).ToArray();
        CollectionAssert.AreEqual(ExpectedRelativePaths, paths);
        Assert.IsTrue(WorkflowTemplates.All.All(t => t.Content.Contains("jobs:") && t.Content.Contains("uses:")));
    }

    [TestMethod]
    public void All_RixCaller_TargetsJobReusableWorkflow()
    {
        var content = ContentOf("rix.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/job.yml@main");
        StringAssert.Contains(content, "workflow_dispatch:");
    }

    [TestMethod]
    public void All_CiFailureCaller_TargetsOnCiFailureReusableWorkflow()
    {
        var content = ContentOf("rix-on-ci-failure.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@main");
        StringAssert.Contains(content, "workflow_run:");
    }
}
