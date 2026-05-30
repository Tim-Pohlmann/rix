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

        try
        {
            Environment.SetEnvironmentVariable("RIX_REPO", "env/repo");
            Environment.SetEnvironmentVariable("RIX_PROMPT", "env prompt");
            Environment.SetEnvironmentVariable("RIX_READ_TOKEN", "env-read");
            Environment.SetEnvironmentVariable("RIX_WRITE_TOKEN", "env-write");
            Environment.SetEnvironmentVariable("RIX_MAX_TOKENS", "999");
            Environment.SetEnvironmentVariable("RIX_TIMEOUT", "15");
            Environment.SetEnvironmentVariable("RIX_WORK_DIR", Path.GetTempPath());
            await parser.InvokeAsync("job");
        }
        finally
        {
            foreach (var key in new[] { "RIX_REPO", "RIX_PROMPT", "RIX_READ_TOKEN", "RIX_WRITE_TOKEN", "RIX_MAX_TOKENS", "RIX_TIMEOUT", "RIX_WORK_DIR" })
                Environment.SetEnvironmentVariable(key, null);
        }

        Assert.IsNotNull(captured);
        Assert.AreEqual("env/repo", captured.Repo.ToString());
        Assert.AreEqual("env prompt", captured.Prompt);
        Assert.AreEqual("env-read", captured.ReadToken.Value);
        Assert.AreEqual("env-write", captured.WriteToken.Value);
        Assert.AreEqual(999, captured.MaxTokens.Value);
        Assert.AreEqual(15, captured.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.WorkDir);
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

        try
        {
            Environment.SetEnvironmentVariable("RIX_REPO", "env/repo");
            await parser.InvokeAsync("job --repo flag/repo --prompt p --read-token r --write-token w");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RIX_REPO", null);
        }

        Assert.IsNotNull(captured);
        Assert.AreEqual("flag/repo", captured.Repo.ToString());
    }
}
