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

    /// <summary><c>submit</c> is the one command that writes, so both halves carry the write token:
    /// git pushes the branches, the repo host opens the pull requests. Asserted here rather than
    /// left to the end-to-end run in <see cref="StartupTests"/>, which never gets far enough to say
    /// which implementations it was given.</summary>
    [TestMethod]
    public void DefaultSubmitContext_PushesWithGit_AndOpensPullRequestsThroughTheRepoHost()
    {
        var context = Composition.DefaultSubmitContext(TestConfig.ValidSubmit());

        Assert.IsInstanceOfType<GitCli>(context.Git);
        Assert.IsInstanceOfType<GitHubSubmitRepoHost>(context.RepoHost);
        Assert.IsInstanceOfType<LocalFileSystem>(context.FileSystem);
    }
}
