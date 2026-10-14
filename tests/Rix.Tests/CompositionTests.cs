using Rix.Agents;
using Rix.Job;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class CompositionTests
{
    /// <summary>The one thing a <see cref="JobContext"/> takes from its config: which coding agent
    /// to run. Every kind is pinned here rather than one per <c>[DataRow]</c>, which can't carry an
    /// internal <see cref="AgentKind"/> into a public test method — the mapping is a switch a new
    /// kind can be added to without anything else noticing.</summary>
    [TestMethod]
    public void DefaultContext_RunsTheAgentTheConfigNames()
    {
        Assert.IsInstanceOfType<ClaudeAgent>(Composition.DefaultContext(TestConfig.Valid(agent: AgentKind.Claude)).Agent);
        Assert.IsInstanceOfType<OpenCodeAgent>(Composition.DefaultContext(TestConfig.Valid(agent: AgentKind.OpenCode)).Agent);
        Assert.IsInstanceOfType<PiAgent>(Composition.DefaultContext(TestConfig.Valid(agent: AgentKind.Pi)).Agent);
    }

    /// <summary><c>ci-failure</c> reads and judges, so its two halves are both read-only hosts —
    /// no agent, no clone, nothing that could run what it found. Separate fields over one object
    /// because the roles are separable even though GitHub serves both.</summary>
    [TestMethod]
    public void DefaultCiFailureContext_ReadsFromTheCiHostAndJudgesAgainstTheRepoHost()
    {
        var context = Composition.DefaultCiFailureContext(TestConfig.ValidCiFailure());

        Assert.IsInstanceOfType<GitHubActionsCiHost>(context.Ci);
        Assert.IsInstanceOfType<GitHubCiFailureRepoHost>(context.RepoHost);
    }
}
