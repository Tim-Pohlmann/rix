using Rix.Cli;
using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class JobCommandTests
{
    /// <summary>Reads <c>job</c> the way <see cref="Startup"/> does, prompt file included.</summary>
    private static JobConfig Read(params string[] args)
    {
        var parsed = CommandArgs.Parse(JobCommand.Build(), ["job", .. args]);
        return JobCommand.ReadConfig(parsed, Path.GetTempPath(), UserHome, Startup.PromptText(new LocalFileSystem(), JobOptions.ReadPrompt(parsed)));
    }

    /// <summary>Reads <c>job</c> with valid values for every required flag, then
    /// <paramref name="extra"/>.</summary>
    private static JobConfig ReadValid(params string[] extra)
    => Read(["--repo", "o/r", "--prompt", "p", "--read-token", "r", "--output-dir", Path.GetTempPath(), .. extra]);

    private const string UserHome = "/the/user/home";

    private static readonly string[] ExpectedDirectoriesWithoutAgentHome = ["--work-dir", "--output-dir"];

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

    /// <summary>Whether they exist is left to <see cref="Startup"/>, so the command accepts
    /// directories that don't and names each one after where it came from for Startup to check.</summary>
    [TestMethod]
    public void RequiredDirectories_NamesEachDirectoryAfterWhereItCameFrom()
    {
        var config = Read(
            "--repo", "o/r", "--prompt", "p", "--read-token", "r",
            "--work-dir", "/nonexistent/work", "--output-dir", "/nonexistent/out", "--factory-repo", "acme/factory");

        CollectionAssert.AreEqual
        (
            new[]
            {
                new RequiredDirectory("--work-dir", new DirectoryPath("/nonexistent/work")),
                new RequiredDirectory("--output-dir", new DirectoryPath("/nonexistent/out")),
                new RequiredDirectory("runner home directory", new DirectoryPath(UserHome)),
            },
            JobCommand.RequiredDirectories(config).ToArray()
        );
    }

    [TestMethod]
    public void RequiredDirectories_LeavesTheRunnerHomeUnchecked_WithoutAFactoryRepo()
    {
        CollectionAssert.AreEqual(
            ExpectedDirectoriesWithoutAgentHome,
            JobCommand.RequiredDirectories(ReadValid()).Select(directory => directory.Name).ToArray());
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

        var config = Read("--repo", "o/r", "--read-token", "r", "--prompt-file", promptFile, "--output-dir", Path.GetTempPath());

        Assert.AreEqual(written, config.Agent.Prompt);
    }

    /// <summary>--prompt-file also satisfies --prompt's own requirement: the two name one value, so
    /// a caller who supplied the file has supplied the prompt.</summary>
    [TestMethod]
    public async Task Command_ReadsThePromptFile_FromTheEnvironment()
    {
        var promptFile = Path.Combine(Directory.CreateTempSubdirectory("rix-prompt-").FullName, "prompt.md");
        await File.WriteAllTextAsync(promptFile, "from the environment");

        using var env = new EnvScope();
        env.Set("RIX_PROMPT_FILE", promptFile);
        var config = Read("--repo", "o/r", "--read-token", "r", "--output-dir", Path.GetTempPath());

        Assert.AreEqual("from the environment", config.Agent.Prompt);
    }

    /// <summary>The cases where naming a prompt file doesn't yield a prompt. Each is reported like
    /// any other bad flag rather than left to run the agent on nothing (or on whichever of the two
    /// inputs a precedence rule happened to pick).</summary>
    [TestMethod]
    public async Task Command_ReportsTheFlag_WhenThePromptFileCannotSupplyThePrompt()
    {
        var dir = Directory.CreateTempSubdirectory("rix-prompt-").FullName;
        var missing = Path.Combine(dir, "absent.md");
        var blank = Path.Combine(dir, "blank.md");
        await File.WriteAllTextAsync(blank, "   \n");
        var real = Path.Combine(dir, "prompt.md");
        await File.WriteAllTextAsync(real, "fix it");

        (string[] Extra, string Expected)[] cases =
        [
            (["--prompt-file", missing], $"--prompt-file: no file at '{missing}'"),
            (["--prompt-file", blank], $"--prompt-file: '{blank}' holds no prompt"),
            (["--prompt-file", real, "--prompt", "fix it"], "--prompt and --prompt-file both give the prompt"),
        ];

        foreach (var (extra, expected) in cases)
        {
            var ex = Assert.ThrowsExactly<InvalidInputException>(
                () => Read(["--repo", "o/r", "--read-token", "r", "--output-dir", Path.GetTempPath(), .. extra]));

            StringAssert.StartsWith(ex.Message, expected);
        }
    }

    [TestMethod]
    public void Command_ReportsOnlyTheFirstProblem()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Read("--repo", "", "--prompt", "p", "--read-token", ""));

        Assert.AreEqual("--repo is required", ex.Message);
    }
}
