using Rix.Initialize;

namespace Rix.Tests;

[TestClass]
public class WorkflowTemplatesTests
{
    private static readonly string[] ExpectedRelativePaths =
        [".github/workflows/rix-agent.yml", ".github/workflows/rix-on-ci-failure.yml"];

    private static readonly string[] ExpectedFactoryServedPaths = [".github/workflows/rix-notify-factory.yml"];

    /// <summary>Looks a template up by the path it lands on rather than by position, so a template
    /// added to <c>Cli/Templates/</c> shifts the discovered order without breaking every assertion
    /// about the two that were already there. Filled in, because what the assertions are about is
    /// the text a caller is actually handed.</summary>
    private static string ContentOf(IInitializeSetup setup, string fileName)
    => WorkflowTemplates.For(setup).Single(t => t.RelativePath == $".github/workflows/{fileName}").Content;

    private static readonly RixRef Ref = new("v9");

    [TestMethod]
    public void For_ResolvesBothEmbeddedTemplates_Nonempty()
    => AssertBothTemplatesResolve(new StandaloneSetup(Ref, CiWorkflowName.Default));

    [TestMethod]
    public void For_Factory_ResolvesBothEmbeddedTemplates_Nonempty()
    => AssertBothTemplatesResolve(new FactorySetup(Ref));

    private static void AssertBothTemplatesResolve(IInitializeSetup setup)
    {
        var templates = WorkflowTemplates.For(setup);
        CollectionAssert.AreEqual(ExpectedRelativePaths, templates.Select(t => t.RelativePath).ToArray());
        Assert.IsTrue(templates.All(t => t.Content.Contains("jobs:") && t.Content.Contains("uses:")));
    }

    /// <summary>Each setup reads its own folder, and a folder is only ever read because a setup
    /// maps to it. A template under a folder no setup maps to would be embedded and never written,
    /// so every embedded one has to belong to some setup.</summary>
    [TestMethod]
    public void Setups_CoverEveryEmbeddedTemplate()
    {
        var embedded = typeof(WorkflowTemplates).Assembly.GetManifestResourceNames()
            .Count(name => name.StartsWith(WorkflowTemplates.ResourcePrefix, StringComparison.Ordinal));

        IInitializeSetup[] setups = [new StandaloneSetup(Ref, CiWorkflowName.Default), new FactorySetup(Ref), new FactoryServedSetup(new("acme/rix-factory"), CiWorkflowName.Default)];

        Assert.AreEqual(setups.Sum(setup => WorkflowTemplates.For(setup).Count), embedded);
    }

    [TestMethod]
    public void For_RixCaller_TargetsAgentReusableWorkflow_AtTheGivenRef()
    {
        var content = ContentOf(new StandaloneSetup(Ref, CiWorkflowName.Default), "rix-agent.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/agent.yml@v9");
        StringAssert.Contains(content, "workflow_dispatch:");
        StringAssert.Contains(content, "repo: ${{ github.repository }}");
    }

    [TestMethod]
    public void For_CiFailureCaller_TargetsOnCiFailureReusableWorkflow_AtTheGivenRef()
    {
        var content = ContentOf(new StandaloneSetup(Ref, CiWorkflowName.Default), "rix-on-ci-failure.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@v9");
        StringAssert.Contains(content, "workflow_run:");
        // Rix refuses a fork's run itself (see CiFailureRunner), but the generated caller must
        // not hand one a runner and this repo's secrets in the first place.
        StringAssert.Contains(content, "github.event.workflow_run.head_repository.full_name == github.repository");
    }

    /// <summary>Spaces and all: a workflow's name is free text, so it lands quoted.</summary>
    [TestMethod]
    public void For_CiFailureCaller_WatchesTheGivenWorkflow()
    {
        var content = ContentOf(new StandaloneSetup(Ref, new("Build and test")), "rix-on-ci-failure.yml");
        StringAssert.Contains(content, "workflows: [\"Build and test\"]");
    }

    [TestMethod]
    public void For_FactoryRixCaller_TargetsTheRepoTheDispatchNames_DefaultingToItself()
    {
        var content = ContentOf(new FactorySetup(Ref), "rix-agent.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/agent.yml@v9");
        StringAssert.Contains(content, "repo: ${{ inputs.repo || github.repository }}");
    }

    /// <summary>Both kinds of factory run copy the factory's own agent home, which is where the
    /// onboarding skill reaches the agent.</summary>
    [TestMethod]
    [DataRow("rix-agent.yml")]
    [DataRow("rix-on-ci-failure.yml")]
    public void For_FactoryCallers_CopyTheFactorysAgentHome(string fileName)
    => StringAssert.Contains(ContentOf(new FactorySetup(Ref), fileName), "factory-repo: ${{ github.repository }}");

    [TestMethod]
    public void For_FactoryCiFailureCaller_AnswersTheDispatchedRun()
    {
        var content = ContentOf(new FactorySetup(Ref), "rix-on-ci-failure.yml");
        StringAssert.Contains(content, "uses: Tim-Pohlmann/rix/.github/workflows/on-ci-failure.yml@v9");
        StringAssert.Contains(content, "types: [rix-ci-failure]");
        StringAssert.Contains(content, "repo: ${{ github.event.client_payload.repo }}");
        StringAssert.Contains(content, "run-id: ${{ github.event.client_payload.run_id }}");
    }

    [TestMethod]
    public void For_FactoryServed_WritesOnlyTheNotifyWorkflow_DispatchingToTheNamedFactory()
    {
        var templates = WorkflowTemplates.For(new FactoryServedSetup(new("acme/rix-factory"), CiWorkflowName.Default));

        CollectionAssert.AreEqual(ExpectedFactoryServedPaths, templates.Select(t => t.RelativePath).ToArray());
        var content = templates[0].Content;
        StringAssert.Contains(content, "FACTORY_REPO: acme/rix-factory");
        StringAssert.Contains(content, "workflow_run:");
        StringAssert.Contains(content, "github.event.workflow_run.head_repository.full_name == github.repository");
        Assert.IsFalse(content.Contains("__RIX_", StringComparison.Ordinal));
    }

    [TestMethod]
    public void For_FactoryServed_NotifiesOnTheGivenWorkflow()
    {
        var content = ContentOf(new FactoryServedSetup(new("acme/rix-factory"), new("Build and test")), "rix-notify-factory.yml");
        StringAssert.Contains(content, "workflows: [\"Build and test\"]");
    }

    /// <summary>The event type is the whole contract between the two sides, and each lives in a
    /// different repo, so a mismatch would only show as dispatches nothing answers.</summary>
    [TestMethod]
    public void For_FactoryServed_DispatchesTheEventTypeTheFactoryAnswers()
    {
        var notify = ContentOf(new FactoryServedSetup(new("acme/rix-factory"), CiWorkflowName.Default), "rix-notify-factory.yml");
        var receiver = ContentOf(new FactorySetup(Ref), "rix-on-ci-failure.yml");

        StringAssert.Contains(notify, "-f event_type=rix-ci-failure");
        StringAssert.Contains(receiver, "types: [rix-ci-failure]");
    }

    [TestMethod]
    [DataRow("acme/rix factory")]
    [DataRow("acme/\"rix\"")]
    [DataRow("acme/rix-factory\n")]
    public void FactoryServedSetup_RejectsANameThatNeedsYamlQuoting(string name)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => _ = new FactoryServedSetup(new(name), CiWorkflowName.Default));

        StringAssert.Contains(ex.Message, "is not a GitHub repo name");
    }

    [TestMethod]
    public void For_LeavesTheEmbeddedTemplatesIntact_SoEachCallSeesItsOwnRef()
    {
        // The substitution is per call, not applied once to the cached copy: two inits in the same
        // process with different refs must not have the first one's ref baked into the second's.
        Assert.AreEqual
        (
            WorkflowTemplates.For(new StandaloneSetup(new("v1"), CiWorkflowName.Default))[0].Content.Replace("@v1", "@v2", StringComparison.Ordinal),
            WorkflowTemplates.For(new StandaloneSetup(new("v2"), CiWorkflowName.Default))[0].Content
        );
    }

    [TestMethod]
    public void FillIn_ReplacesEveryOccurrence_WhenTheValueIsGiven()
    => Assert.AreEqual("a@v9 b@v9", WorkflowTemplates.FillIn("a@__X__ b@__X__", "__X__", "v9", "t.yml"));

    [TestMethod]
    public void FillIn_LeavesTheContent_WhenThereIsNoValueAndNoPlaceholder()
    => Assert.AreEqual("a b", WorkflowTemplates.FillIn("a b", "__X__", null, "t.yml"));

    [TestMethod]
    public void FillIn_Throws_WhenATemplateCarriesAPlaceholderItsSetupHasNoValueFor()
    {
        var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => WorkflowTemplates.FillIn("a@__X__", "__X__", null, "t.yml"));
        StringAssert.Contains(thrown.Message, "t.yml");
        StringAssert.Contains(thrown.Message, "__X__");
    }
}
