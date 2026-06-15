using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Rix.Cli;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobCommandTests
{
    private static Parser BuildParser(Func<JobConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(JobCommand.Build(handler));
        return new CommandLineBuilder(root).UseDefaults().Build();
    }

    [TestMethod]
    public async Task Command_PassesEnvVarFallbacks_WhenFlagsAbsent()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        env.Set("RIX_PROMPT", "env prompt");
        env.Set("RIX_READ_TOKEN", "env-read");
        env.Set("RIX_MAX_TOKENS", "999");
        env.Set("RIX_TIMEOUT", "15");
        env.Set("RIX_WORK_DIR", Path.GetTempPath());
        env.Set("RIX_OUTPUT_DIR", Path.GetTempPath());
        await parser.InvokeAsync("job");

        Assert.IsNotNull(captured);
        Assert.AreEqual("env/repo", captured.Repo.ToString());
        Assert.AreEqual("env prompt", captured.Prompt);
        Assert.AreEqual("env-read", captured.ReadToken.Value);
        Assert.AreEqual(999, captured.MaxTokens.Value);
        Assert.AreEqual(15, captured.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.OutputDir.Value);
    }

    [TestMethod]
    public async Task Command_FlagsTakePrecedenceOverEnvVars()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        await parser.InvokeAsync(
            ["job", "--repo", "flag/repo", "--prompt", "p", "--read-token", "r", "--output-dir", Path.GetTempPath()]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("flag/repo", captured.Repo.ToString());
    }

    [TestMethod]
    public async Task Command_SelectsAgent_FromFlag()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r",
             "--output-dir", Path.GetTempPath(), "--agent", "opencode"]);

        Assert.IsNotNull(captured);
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, captured.Agent);
    }

    [TestMethod]
    public async Task Command_SelectsAgent_FromEnvVar_WhenFlagAbsent()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_AGENT", "opencode");
        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r", "--output-dir", Path.GetTempPath()]);

        Assert.IsNotNull(captured);
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, captured.Agent);
    }

    [TestMethod]
    public async Task Command_DefaultsToClaude_WhenAgentUnset()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r", "--output-dir", Path.GetTempPath()]);

        Assert.IsNotNull(captured);
        Assert.AreEqual(Rix.Agents.AgentKind.Claude, captured.Agent);
    }
}
