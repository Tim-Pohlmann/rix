using Rix.Cli;
using Rix.CiFailure;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class CiFailureCommandTests
{
    private static Parser BuildParser(Func<CiFailureConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(CiFailureCommand.Build(handler));
        return new CommandLineBuilder(root).UseDefaults().Build();
    }

    [TestMethod]
    public async Task Command_PassesEnvVarFallbacks_WhenFlagsAbsent()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        env.Set("RIX_READ_TOKEN", "env-read");
        env.Set("RIX_RUN_ID", "42");
        await parser.InvokeAsync("ci-failure");

        Assert.IsNotNull(captured);
        Assert.AreEqual("env/repo", captured.Repo.ToString());
        Assert.AreEqual("env-read", captured.ReadToken.Value);
        Assert.AreEqual(42, captured.RunId);
    }

    [TestMethod]
    public async Task Command_FlagsTakePrecedenceOverEnvVars()
    {
        CiFailureConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_REPO", "env/repo");
        await parser.InvokeAsync(["ci-failure", "--repo", "flag/repo", "--read-token", "r", "--run-id", "1"]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("flag/repo", captured.Repo.ToString());
    }
}
