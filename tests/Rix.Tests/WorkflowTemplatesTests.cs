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
    private static string ContentOf(IInitializeSetup setup, string fileName)
    => WorkflowTemplates.For(setup, Ref).Single(t => t.RelativePath == $".github/workflows/{fileName}").Content;

    private static readonly WorkflowRef Ref = new("v9");

    [TestMethod]
    public void For_ResolvesBothEmbeddedTemplates_Nonempty()
    => AssertBothTemplatesResolve(new StandaloneSetup());

    [TestMethod]
    public void For_Factory_ResolvesBothEmbeddedTemplates_Nonempty()
    => AssertBothTemplatesResolve(new FactorySetup());

    private static void AssertBothTemplatesResolve(IInitializeSetup setup)
    {
        var templates = WorkflowTemplates.For(setup, Ref);
        CollectionAssert.AreEqual(ExpectedRelativePaths, templates.Select(t => t.RelativePath).ToArray());
        Assert.IsTrue(templates.All(t => t.Content.Contains("jobs:") && t.Content.Contains("uses:")));
    }

    /// <summary>Each setup reads its own folder, and a folder is only ever read because a setup
    /// names it in <see cref="WorkflowTemplates.Of"/>. A template under a folder nothing names
    /// would be embedded and never written, so every embedded one has to belong to some
    /// setup.</summary>
    [TestMethod]
    public void Setups_CoverEveryEmbeddedTemplate()
    {
        var embedded = typeof(WorkflowTemplates).Assembly.GetManifestResourceNames()
            .Count(name => name.StartsWith(WorkflowTemplates.ResourcePrefix, StringComparison.Ordinal));

        Assert.AreEqual(WorkflowTemplates.Standalone.Count + WorkflowTemplates.Factory.Count + WorkflowTemplates.FactoryServed.Count, embedded);
    }

    [TestMethod]
    public void For_RixCaller_TargetsAgentReusableWorkflow_AtTheGivenRef()
    {
        var content = ContentOf(new StandaloneSetup(), "rix.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/agent.yml@v9");
        StringAssert.Contains(content, "workflow_dispatch:");
        StringAssert.Contains(content, "repo: ${{ github.repository }}");
    }

    [TestMethod]
    public void For_CiFailureCaller_TargetsOnCiFailureReusableWorkflow_AtTheGivenRef()
    {
        var content = ContentOf(new StandaloneSetup(), "rix-on-ci-failure.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@v9");
        StringAssert.Contains(content, "workflow_run:");
        // Rix refuses a fork's run itself (see CiFailureRunner), but the generated caller must
        // not hand one a runner and this repo's secrets in the first place.
        StringAssert.Contains(content, "github.event.workflow_run.head_repository.full_name == github.repository");
    }

    [TestMethod]
    public void For_FactoryRixCaller_TargetsTheRepoTheDispatchNames_DefaultingToItself()
    {
        var content = ContentOf(new FactorySetup(), "rix.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/agent.yml@v9");
        StringAssert.Contains(content, "repo: ${{ inputs.repo || github.repository }}");
    }

    [TestMethod]
    public void For_FactoryCiFailureCaller_AnswersTheDispatchedRun()
    {
        var content = ContentOf(new FactorySetup(), "rix-on-ci-failure.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@v9");
        StringAssert.Contains(content, "types: [rix-ci-failure]");
        StringAssert.Contains(content, "repo: ${{ github.event.client_payload.repo }}");
        StringAssert.Contains(content, "run-id: ${{ github.event.client_payload.run_id }}");
    }

    [TestMethod]
    public void For_FactoryServed_WritesOnlyTheNotifyWorkflow_DispatchingToTheNamedFactory()
    {
        var templates = WorkflowTemplates.For(new FactoryServedSetup(new("acme/rix-factory")), Ref);

        CollectionAssert.AreEqual(new[] { ".github/workflows/notify-rix-factory.yml" }, templates.Select(t => t.RelativePath).ToArray());
        var content = templates[0].Content;
        StringAssert.Contains(content, "FACTORY_REPO: acme/rix-factory");
        StringAssert.Contains(content, "workflow_run:");
        StringAssert.Contains(content, "github.event.workflow_run.head_repository.full_name == github.repository");
        Assert.IsFalse(content.Contains("__RIX_", StringComparison.Ordinal));
    }

    /// <summary>The event type is the whole contract between the two sides, and each lives in a
    /// different repo, so a mismatch would only show as dispatches nothing answers.</summary>
    [TestMethod]
    public void For_FactoryServed_DispatchesTheEventTypeTheFactoryAnswers()
    {
        var notify = ContentOf(new FactoryServedSetup(new("acme/rix-factory")), "notify-rix-factory.yml");
        var receiver = ContentOf(new FactorySetup(), "rix-on-ci-failure.yml");

        StringAssert.Contains(notify, "-f event_type=rix-ci-failure");
        StringAssert.Contains(receiver, "types: [rix-ci-failure]");
    }

    [TestMethod]
    [DataRow("acme/rix factory")]
    [DataRow("acme/\"rix\"")]
    [DataRow("acme/rix-factory\n")]
    public void FactoryServedSetup_RejectsANameThatNeedsYamlQuoting(string name)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => new FactoryServedSetup(new(name)));

        StringAssert.Contains(ex.Message, "is not a GitHub repo name");
    }

    [TestMethod]
    public void For_LeavesTheEmbeddedTemplatesIntact_SoEachCallSeesItsOwnRef()
    {
        // The substitution is per call, not applied once to the cached copy: two inits in the same
        // process with different refs must not have the first one's ref baked into the second's.
        Assert.AreEqual
        (
            WorkflowTemplates.For(new StandaloneSetup(), new("v1"))[0].Content.Replace("@v1", "@v2", StringComparison.Ordinal),
            WorkflowTemplates.For(new StandaloneSetup(), new("v2"))[0].Content
        );
    }
}
