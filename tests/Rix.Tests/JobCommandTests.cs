using Rix.Cli;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

[TestClass]
public class JobCommandTests
{
    private static Parser BuildParser(Func<JobConfig, Task<int>> handler)
    {
        var root = new RootCommand();
        root.AddCommand(JobCommand.Build(handler));
        return CliPipeline.Build(root);
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
        env.Set("RIX_ALLOWED_PUSH_BRANCHES", "rix/env-a,rix/env-b");
        await parser.InvokeAsync("job");

        Assert.IsNotNull(captured);
        Assert.AreEqual("env/repo", captured.Repo.ToString());
        Assert.AreEqual("env prompt", captured.Agent.Prompt);
        Assert.AreEqual("env-read", captured.ReadToken.Value);
        Assert.AreEqual(999, captured.Agent.MaxTokens.Value);
        Assert.AreEqual(15, captured.TimeoutMinutes.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.WorkDir.Value);
        Assert.AreEqual(Path.GetTempPath(), captured.OutputDir.Value);
        CollectionAssert.AreEqual(
            new[] { "rix/env-a", "rix/env-b" },
            captured.AllowedPushBranches.Select(b => b.Value).ToArray());
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
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, captured.Agent.Kind);
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
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, captured.Agent.Kind);
    }

    [TestMethod]
    public async Task Command_PassesThroughModel_FromFlag()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r",
             "--output-dir", Path.GetTempPath(), "--model", "openai/gpt-4o"]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("openai/gpt-4o", captured.Agent.Model);
    }

    [TestMethod]
    public async Task Command_DefaultsToOpenCode_WhenAgentUnset()
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
        Assert.AreEqual(Rix.Agents.AgentKind.OpenCode, captured.Agent.Kind);
    }

    [TestMethod]
    public async Task Command_PassesThroughAllowedPushBranches_FromFlag()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r",
             "--output-dir", Path.GetTempPath(), "--allowed-push-branches", "rix/flag-a,rix/flag-b"]);

        Assert.IsNotNull(captured);
        CollectionAssert.AreEqual(
            new[] { "rix/flag-a", "rix/flag-b" },
            captured.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public async Task Command_PassesThroughAllowedPushBranches_FromEnvVar_WhenFlagAbsent()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_ALLOWED_PUSH_BRANCHES", "rix/env-a,rix/env-b");
        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r", "--output-dir", Path.GetTempPath()]);

        Assert.IsNotNull(captured);
        CollectionAssert.AreEqual(
            new[] { "rix/env-a", "rix/env-b" },
            captured.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public async Task Command_FlagTakesPrecedenceOverEnvVar_ForAllowedPushBranches()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_ALLOWED_PUSH_BRANCHES", "rix/env-a");
        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r",
             "--output-dir", Path.GetTempPath(), "--allowed-push-branches", "rix/flag-a"]);

        Assert.IsNotNull(captured);
        CollectionAssert.AreEqual(
            new[] { "rix/flag-a" },
            captured.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public async Task Command_DefaultsAllowedPushBranchesToEmpty_WhenUnset()
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
        Assert.AreEqual(0, captured.AllowedPushBranches.Count);
    }

    [TestMethod]
    public async Task Command_DropsBlankAndDuplicateAllowedPushBranches()
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r",
             "--output-dir", Path.GetTempPath(), "--allowed-push-branches", "rix/a,,rix/a, rix/b"]);

        Assert.IsNotNull(captured);
        string[] expected = ["rix/a", "rix/b"];
        CollectionAssert.AreEqual(expected, captured.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    public async Task Command_AcceptsAllowedPushBranches_ThatAreNotRixBranches()
    {
        // The rix/* naming pattern is only a requirement for branches the agent creates via /pr;
        // /push always delivers to a branch that already exists on the remote, so any name is fine.
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--prompt", "p", "--read-token", "r",
             "--output-dir", Path.GetTempPath(), "--allowed-push-branches", "rix/good,main,prod"]);

        Assert.IsNotNull(captured);
        string[] expected = ["rix/good", "main", "prod"];
        CollectionAssert.AreEqual(expected, captured.AllowedPushBranches.Select(b => b.Value).ToArray());
    }

    [TestMethod]
    [DataRow("", "p", "r", "error: --repo is required")]
    [DataRow("noslash", "p", "r", "error: --repo: 'noslash' is not a valid repo identifier")]
    [DataRow("o/r", "", "r", "error: --prompt is required")]
    [DataRow("o/r", "p", "", "error: --read-token is required")]
    public async Task Command_Returns2_AndReportsTheFlag_WhenInputInvalid(string repo, string prompt, string readToken, string expectedError)
    {
        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(
            ["job", "--repo", repo, "--prompt", prompt, "--read-token", readToken, "--output-dir", Path.GetTempPath()]);

        Assert.IsNull(captured);
        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        StringAssert.Contains(stderr.Text, expectedError);
    }

    /// <summary>The prompt rix ci-failure builds is assembled from a failing run's log output, so
    /// it is both long and arbitrary: it travels to this command as a file, and the one thing that
    /// has to hold is that the file's bytes reach the agent unchanged. Asserted with the shapes a
    /// log tail really produces — a trailing newline, a blank line, a line that looks like a flag,
    /// and the shell metacharacters a run's output is full of.</summary>
    [TestMethod]
    public async Task Command_ReadsThePromptFromAFile_Verbatim()
    {
        var promptFile = Path.Combine(Directory.CreateTempSubdirectory("rix-prompt-").FullName, "prompt.md");
        const string written = "fix it\n\n--not-a-flag $(echo pwned) `id` \"quoted\"\n";
        await File.WriteAllTextAsync(promptFile, written);

        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        var exitCode = await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--read-token", "r", "--prompt-file", promptFile,
             "--output-dir", Path.GetTempPath()]);

        Assert.AreEqual(0, exitCode);
        Assert.IsNotNull(captured);
        Assert.AreEqual(written, captured.Agent.Prompt);
    }

    /// <summary>--prompt-file also satisfies --prompt's own requirement: the two name one value, so
    /// a caller who supplied the file has supplied the prompt.</summary>
    [TestMethod]
    public async Task Command_ReadsThePromptFile_FromTheEnvironment()
    {
        var promptFile = Path.Combine(Directory.CreateTempSubdirectory("rix-prompt-").FullName, "prompt.md");
        await File.WriteAllTextAsync(promptFile, "from the environment");

        JobConfig? captured = null;
        var parser = BuildParser(config =>
        {
            captured = config;
            return Task.FromResult(0);
        });

        using var env = new EnvScope();
        env.Set("RIX_PROMPT_FILE", promptFile);
        await parser.InvokeAsync(
            ["job", "--repo", "o/r", "--read-token", "r", "--output-dir", Path.GetTempPath()]);

        Assert.IsNotNull(captured);
        Assert.AreEqual("from the environment", captured.Agent.Prompt);
    }

    /// <summary>The cases where naming a prompt file doesn't yield a prompt. Each is reported like
    /// any other bad flag rather than left to run the agent on nothing (or on whichever of the two
    /// inputs a precedence rule happened to pick).</summary>
    [TestMethod]
    public async Task Command_Returns2_WhenThePromptFileCannotSupplyThePrompt()
    {
        var dir = Directory.CreateTempSubdirectory("rix-prompt-").FullName;
        var missing = Path.Combine(dir, "absent.md");
        var blank = Path.Combine(dir, "blank.md");
        await File.WriteAllTextAsync(blank, "   \n");
        var real = Path.Combine(dir, "prompt.md");
        await File.WriteAllTextAsync(real, "fix it");

        (string[] Extra, string Expected)[] cases =
        [
            (["--prompt-file", missing], $"error: --prompt-file: no file at '{missing}'"),
            (["--prompt-file", blank], $"error: --prompt-file: '{blank}' holds no prompt"),
            (["--prompt-file", real, "--prompt", "fix it"], "error: --prompt and --prompt-file both give the prompt"),
        ];

        foreach (var (extra, expected) in cases)
        {
            JobConfig? captured = null;
            var parser = BuildParser(config =>
            {
                captured = config;
                return Task.FromResult(0);
            });

            using var stderr = new ConsoleErrorScope();
            string[] args =
                ["job", "--repo", "o/r", "--read-token", "r", "--output-dir", Path.GetTempPath(), .. extra];
            var exitCode = await parser.InvokeAsync(args);

            Assert.IsNull(captured, expected);
            Assert.AreEqual(ExitCodes.SetupFailed, exitCode, expected);
            StringAssert.Contains(stderr.Text, expected);
        }
    }

    [TestMethod]
    public async Task Command_ReportsOnlyTheFirstProblem()
    {
        var parser = BuildParser(_ => Task.FromResult(0));

        using var stderr = new ConsoleErrorScope();
        var exitCode = await parser.InvokeAsync(["job", "--repo", "", "--prompt", "", "--read-token", ""]);

        Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
        Assert.AreEqual("error: --repo is required", stderr.Text.Trim());
    }
}
