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

        StringAssert.Contains(steps, "on this repo and every repo");
        StringAssert.Contains(steps, "Onboard this repo to the factory");
        StringAssert.Contains(steps, "rix initialize <this repo's owner/name>");
    }

    [TestMethod]
    public void For_ScopesTheDispatchTokenToTheNamedFactory()
    {
        var steps = NextSteps.For(new FactoryServedSetup(new("acme/rix-factory"), CiWorkflowName.Default));

        StringAssert.Contains(steps, "RIX_FACTORY_DISPATCH_TOKEN   PAT, contents:write on acme/rix-factory only");
    }

    /// <summary>A factory's steps have the agent home committed with the workflows, since a run
    /// fails without it.</summary>
    [TestMethod]
    public void For_HasAFactoryCommitItsAgentHome()
    => StringAssert.Contains(NextSteps.For(new FactorySetup(new("v0"))), ".rix/agent-home");

    /// <summary>The steps are prose about the templates, so nothing else would notice a template
    /// renamed under them.</summary>
    [TestMethod]
    public void For_NamesOnlyWorkflowsTheSetupWrites()
    {
        IInitializeSetup[] setups = [new StandaloneSetup(new("v0"), CiWorkflowName.Default), new FactorySetup(new("v0")), new FactoryServedSetup(new("acme/rix-factory"), CiWorkflowName.Default)];

        foreach (var setup in setups)
        {
            var written = InitializeTemplates.For(setup).Select(t => Path.GetFileName(t.RelativePath)).ToArray();
            foreach (var named in WorkflowFileName().Matches(NextSteps.For(setup)).Select(m => m.Value))
                CollectionAssert.Contains(written, named, $"{setup.GetType().Name}'s next steps name {named}");
        }
    }

    [GeneratedRegex(@"[a-z][a-z-]*\.yml")]
    private static partial Regex WorkflowFileName();
}
