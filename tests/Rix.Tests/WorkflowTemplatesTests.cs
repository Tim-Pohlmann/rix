using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class WorkflowTemplatesTests
{
    [TestMethod]
    public void All_ResolvesBothEmbeddedTemplates_Nonempty()
    {
        var paths = WorkflowTemplates.All.Select(t => t.RelativePath).ToArray();
        CollectionAssert.AreEqual
        (
            new[] { ".github/workflows/rix.yml", ".github/workflows/rix-on-ci-failure.yml" },
            paths
        );
        Assert.IsTrue(WorkflowTemplates.All.All(t => t.Content.Contains("jobs:") && t.Content.Contains("uses:")));
    }

    [TestMethod]
    public void All_RixCaller_TargetsJobReusableWorkflow()
    {
        var (_, content) = WorkflowTemplates.All[0];
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/job.yml@main");
        StringAssert.Contains(content, "workflow_dispatch:");
    }

    [TestMethod]
    public void All_CiFailureCaller_TargetsOnCiFailureReusableWorkflow()
    {
        var (_, content) = WorkflowTemplates.All[1];
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@main");
        StringAssert.Contains(content, "workflow_run:");
    }
}
