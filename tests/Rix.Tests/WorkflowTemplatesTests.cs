using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class WorkflowTemplatesTests
{
    private static readonly string[] ExpectedRelativePaths =
        [".github/workflows/rix.yml", ".github/workflows/rix-on-ci-failure.yml"];

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
        var (_, content) = WorkflowTemplates.For(Ref)[0];
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/job.yml@v9");
        StringAssert.Contains(content, "workflow_dispatch:");
    }

    [TestMethod]
    public void For_CiFailureCaller_TargetsOnCiFailureReusableWorkflow_AtTheGivenRef()
    {
        var (_, content) = WorkflowTemplates.For(Ref)[1];
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@v9");
        StringAssert.Contains(content, "workflow_run:");
        // Rix refuses a fork's run itself (see CiFailureDetector), but the generated caller must
        // not hand one a runner and this repo's secrets in the first place.
        StringAssert.Contains(content, "github.event.workflow_run.head_repository.full_name == github.repository");
    }

    [TestMethod]
    public void For_LeavesTheEmbeddedTemplatesIntact_SoEachCallSeesItsOwnRef()
    {
        // The substitution is per call, not applied once to the cached copy: two inits in the same
        // process with different refs must not have the first one's ref baked into the second's.
        Assert.AreEqual(WorkflowTemplates.For(new("v1"))[0].Content.Replace("@v1", "@v2", StringComparison.Ordinal), WorkflowTemplates.For(new("v2"))[0].Content);
    }
}
