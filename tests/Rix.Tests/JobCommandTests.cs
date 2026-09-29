using Rix.Cli;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobCommandTests
{
    private static JobConfig Read(params string[] args)
    => JobCommand.ReadConfig(CommandArgs.Parse(JobCommand.Build(), ["job", .. args]));

    /// <summary>Reads <c>job</c> with valid values for every required flag, then
    /// <paramref name="extra"/>.</summary>
    private static JobConfig ReadValid(params string[] extra)
    => Read(["--repo", "o/r", "--prompt", "p", "--read-token", "r", "--output-dir", Path.GetTempPath(), .. extra]);

    [TestMethod]
    public void Command_PassesEnvVarFallbacks_WhenFlagsAbsent()
    {
        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        env.Set("RIX_PROMPT", "env prompt");
        env.Set("RIX_READ_TOKEN", "env-read");
        env.Set("RIX_MAX_TOKENS", "999");
        env.Set("RIX_TIMEOUT", "15");
        env.Set("RIX_WORK_DIR", Path.GetTempPath());
        env.Set("RIX_OUTPUT_DIR", Path.GetTempPath());
        env.Set("RIX_ALLOWED_PUSH_BRANCHES", "rix/env-a,rix/env-b");
        var config = Read();

        Assert.AreEqual("env/repo", config.Repo.ToString());
        Assert.AreEqual("env prompt", config.Agent.Prompt);
        Assert.AreEqual("env-read", config.ReadToken.Value);
        Assert.AreEqual(999, config.Agent.MaxTokens.Value);
        Assert.AreEqual(15, config.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), config.WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), config.OutputDir.Value);
        CollectionAssert.AreEqual(
            new[] { "rix/env-a", "rix/env-b" },
            config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public void Command_FlagsTakePrecedenceOverEnvVars()
    {
        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        var config = Read("--repo", "flag/repo", "--prompt", "p", "--read-token", "r", "--output-dir", Path.GetTempPath());

        Assert.AreEqual("flag/repo", config.Repo.ToString());
    }

    [TestMethod]
    public void Command_SelectsAgent_FromFlag()
    {
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, ReadValid("--agent", "opencode").Agent.Kind);
    }

    [TestMethod]
    public void Command_SelectsAgent_FromEnvVar_WhenFlagAbsent()
    {
        using var env = new EnvScope();
        env.Set("RIX_AGENT", "opencode");

        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, ReadValid().Agent.Kind);
    }

    [TestMethod]
    public void Command_PassesThroughModel_FromFlag()
    {
        Assert.AreEqual("openai/gpt-4o", ReadValid("--model", "openai/gpt-4o").Agent.Model);
    }

    [TestMethod]
    public void Command_DefaultsToOpenCode_WhenAgentUnset()
    {
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, ReadValid().Agent.Kind);
    }

    [TestMethod]
    public void Command_PassesThroughAllowedPushBranches_FromFlag()
    {
        var config = ReadValid("--allowed-push-branches", "rix/flag-a,rix/flag-b");

        CollectionAssert.AreEqual(
            new[] { "rix/flag-a", "rix/flag-b" },
            config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public void Command_PassesThroughAllowedPushBranches_FromEnvVar_WhenFlagAbsent()
    {
        using var env = new EnvScope();
        env.Set("RIX_ALLOWED_PUSH_BRANCHES", "rix/env-a,rix/env-b");
        var config = ReadValid();

        CollectionAssert.AreEqual(
            new[] { "rix/env-a", "rix/env-b" },
            config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public void Command_FlagTakesPrecedenceOverEnvVar_ForAllowedPushBranches()
    {
        using var env = new EnvScope();
        env.Set("RIX_ALLOWED_PUSH_BRANCHES", "rix/env-a");
        var config = ReadValid("--allowed-push-branches", "rix/flag-a");

        CollectionAssert.AreEqual(
            new[] { "rix/flag-a" },
            config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public void Command_DefaultsAllowedPushBranchesToEmpty_WhenUnset()
    {
        Assert.AreEqual(0, ReadValid().AllowedPushBranches.Count);
    }

    [TestMethod]
    public void Command_DropsBlankAndDuplicateAllowedPushBranches()
    {
        var config = ReadValid("--allowed-push-branches", "rix/a,,rix/a, rix/b");

        string[] expected = ["rix/a", "rix/b"];
        CollectionAssert.AreEqual(expected, config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public void Command_AcceptsAllowedPushBranches_ThatAreNotRixBranches()
    {
        // The rix/* naming pattern is only a requirement for branches the agent creates via /pr;
        // /push always delivers to a branch that already exists on the remote, so any name is fine.
        var config = ReadValid("--allowed-push-branches", "rix/good,main,prod");

        string[] expected = ["rix/good", "main", "prod"];
        CollectionAssert.AreEqual(expected, config.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    [DataRow("", "p", "r", "--repo is required")]
    [DataRow("noslash", "p", "r", "--repo: 'noslash' is not a valid repo identifier")]
    [DataRow("o/r", "", "r", "--prompt is required")]
    [DataRow("o/r", "p", "", "--read-token is required")]
    public void Command_ReportsTheFlag_WhenInputInvalid(string repo, string prompt, string readToken, string expectedError)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(
            () => Read("--repo", repo, "--prompt", prompt, "--read-token", readToken, "--output-dir", Path.GetTempPath()));

        StringAssert.StartsWith(ex.Message, expectedError);
    }

    [TestMethod]
    public void Command_ReportsOnlyTheFirstProblem()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--repo", "", "--prompt", "", "--read-token", ""));

        Assert.AreEqual("--repo is required", ex.Message);
    }
}
