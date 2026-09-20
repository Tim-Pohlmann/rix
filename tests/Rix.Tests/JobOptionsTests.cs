using Rix.Agents;
using Rix.Cli;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

/// <summary>Covers the readers behind every flag <c>job</c> and <c>ci-failure</c> share, the
/// boundary that turns each one into the value their configs take: defaults, parsing, and the
/// message each malformed flag produces. Reached through the <c>job</c> command's own flag surface,
/// so the <see cref="CommonOptions"/> readers registered alongside <see cref="JobOptions"/>' own are
/// covered here too rather than separately. Which problem a command reports first, and the two flags
/// only <c>job</c> owns (<c>--prompt</c>, <c>--allowed-push-branches</c>), are covered by
/// <see cref="JobCommandTests"/> and <see cref="CiFailureCommandTests"/>.</summary>
[TestClass]
public class JobOptionsTests
{
    private static readonly string ExistingDir = Path.GetTempPath();

    private static ParseResult Parse(params string[] args)
    {
        var root = new RootCommand();
        root.AddCommand(JobCommand.Build(_ => Task.FromResult(0)));
        return root.Parse(["job", .. args]);
    }

    private static string ErrorOf(Action read) => Assert.ThrowsExactly<InvalidInputException>(read).Message;

    [TestMethod]
    public void ReadRepo_ParsesOwnerSlashRepo()
    => Assert.AreEqual("owner/repo", CommonOptions.ReadRepo(Parse("--repo", "owner/repo")).Value);

    [TestMethod]
    public void ReadRepo_RejectsEmpty()
    => Assert.AreEqual("--repo is required", ErrorOf(() => CommonOptions.ReadRepo(Parse("--repo", ""))));

    [TestMethod]
    [DataRow("noslash")]
    [DataRow("owner/repo/extra")]
    [DataRow("/repo")]
    [DataRow("owner/")]
    public void ReadRepo_RejectsMalformed(string repo)
    {
        var error = ErrorOf(() => CommonOptions.ReadRepo(Parse("--repo", repo)));
        StringAssert.StartsWith(error, "--repo: ");
        StringAssert.Contains(error, "repo identifier");
    }

    [TestMethod]
    public void ReadReadToken_ReadsValue()
    => Assert.AreEqual("read-tok", JobOptions.ReadReadToken(Parse("--read-token", "read-tok")).Value);

    [TestMethod]
    public void ReadReadToken_RejectsEmpty()
    => Assert.AreEqual("--read-token is required", ErrorOf(() => JobOptions.ReadReadToken(Parse("--read-token", ""))));

    [TestMethod]
    public void ReadMaxTokens_AppliesDefault()
    => Assert.AreEqual(JobConfig.DefaultMaxTokens, JobOptions.ReadMaxTokens(Parse()).Value);

    [TestMethod]
    public void ReadMaxTokens_OverridesDefault()
    => Assert.AreEqual(1000, JobOptions.ReadMaxTokens(Parse("--max-tokens", "1000")).Value);

    [TestMethod]
    [DataRow("0")]
    [DataRow("abc")]
    public void ReadMaxTokens_RejectsNonPositiveInteger(string raw)
    => Assert.AreEqual($"--max-tokens: must be a positive integer, got '{raw}'", ErrorOf(() => JobOptions.ReadMaxTokens(Parse("--max-tokens", raw))));

    [TestMethod]
    public void ReadTimeout_AppliesDefault()
    => Assert.AreEqual(JobConfig.DefaultTimeoutMinutes, JobOptions.ReadTimeout(Parse()).Value);

    [TestMethod]
    public void ReadTimeout_OverridesDefault()
    => Assert.AreEqual(5, JobOptions.ReadTimeout(Parse("--timeout", "5")).Value);

    [TestMethod]
    [DataRow("-1")]
    [DataRow("abc")]
    public void ReadTimeout_RejectsNonPositiveInteger(string raw)
    => Assert.AreEqual($"--timeout: must be a positive integer, got '{raw}'", ErrorOf(() => JobOptions.ReadTimeout(Parse("--timeout", raw))));

    [TestMethod]
    public void ReadWorkDir_DefaultsToTemp_WhenBlank()
    {
        Assert.AreEqual(Path.GetTempPath(), CommonOptions.ReadWorkDir(Parse()).Value);
        Assert.AreEqual(Path.GetTempPath(), CommonOptions.ReadWorkDir(Parse("--work-dir", "")).Value);
        Assert.AreEqual(Path.GetTempPath(), CommonOptions.ReadWorkDir(Parse("--work-dir", "   ")).Value);
    }

    [TestMethod]
    public void ReadWorkDir_UsesExistingDirectory()
    => Assert.AreEqual(Path.GetFullPath(ExistingDir), CommonOptions.ReadWorkDir(Parse("--work-dir", ExistingDir)).Value);

    [TestMethod]
    public void ReadWorkDir_RejectsNonExistent()
    => Assert.AreEqual("--work-dir: directory does not exist: /nonexistent/path/xyz", ErrorOf(() => CommonOptions.ReadWorkDir(Parse("--work-dir", "/nonexistent/path/xyz"))));

    [TestMethod]
    public void ReadOutputDir_UsesExistingDirectory()
    => Assert.AreEqual(Path.GetFullPath(ExistingDir), JobOptions.ReadOutputDir(Parse("--output-dir", ExistingDir)).Value);

    [TestMethod]
    public void ReadOutputDir_RejectsEmpty()
    => Assert.AreEqual("--output-dir is required", ErrorOf(() => JobOptions.ReadOutputDir(Parse("--output-dir", ""))));

    [TestMethod]
    public void ReadOutputDir_RejectsNonExistent()
    => Assert.AreEqual("--output-dir: directory does not exist: /nonexistent/out", ErrorOf(() => JobOptions.ReadOutputDir(Parse("--output-dir", "/nonexistent/out"))));

    [TestMethod]
    public void ReadAgent_DefaultsToOpenCode()
    => Assert.AreEqual(JobConfig.DefaultAgent, JobOptions.ReadAgent(Parse()));

    [TestMethod]
    public void ReadAgent_SelectsAgent()
    {
        Assert.AreEqual(AgentKind.Claude, JobOptions.ReadAgent(Parse("--agent", "claude")));
        Assert.AreEqual(AgentKind.Pi, JobOptions.ReadAgent(Parse("--agent", "pi")));
    }

    [TestMethod]
    public void ReadAgent_RejectsUnknown()
    {
        var error = ErrorOf(() => JobOptions.ReadAgent(Parse("--agent", "devin")));
        StringAssert.StartsWith(error, "--agent: ");
        StringAssert.Contains(error, "devin");
    }

    [TestMethod]
    public void ReadModel_IsNull_WhenBlank()
    {
        // Unset means "let the agent CLI pick its own default" for every agent — opencode and
        // claude both fall back to a free/default model on their own when --model is omitted.
        Assert.IsNull(JobOptions.ReadModel(Parse()));
        Assert.IsNull(JobOptions.ReadModel(Parse("--model", "")));
    }

    [TestMethod]
    public void ReadModel_PassesThroughExplicitValue()
    => Assert.AreEqual("openai/gpt-4o", JobOptions.ReadModel(Parse("--model", "openai/gpt-4o")));

    [TestMethod]
    public void ReadAgentApiKey_IsNull_WhenNotSupplied()
    => Assert.IsNull(JobOptions.ReadAgentApiKey(Parse()));

    [TestMethod]
    public void ReadAgentApiKey_PassesThroughExplicitValue()
    => Assert.AreEqual("secret", JobOptions.ReadAgentApiKey(Parse("--agent-api-key", "secret")));

    [TestMethod]
    public void ReadAgentCredential_IsNull_WhenNoKeySupplied()
    {
        // Even an explicit env name is ignored without a key: there is nothing to export under it.
        var parsed = Parse("--agent-api-key-env", "ANTHROPIC_API_KEY");
        Assert.IsNull(JobOptions.ReadAgentCredential(parsed, AgentKind.OpenCode, apiKey: null));
    }

    [TestMethod]
    public void ReadAgentCredential_DefaultsPerAgent_WhenKeySuppliedWithoutOverride()
    {
        Assert.AreEqual(new AgentCredential("OPENCODE_API_KEY", "secret"), JobOptions.ReadAgentCredential(Parse(), AgentKind.OpenCode, "secret"));
        Assert.AreEqual(new AgentCredential("ANTHROPIC_API_KEY", "secret"), JobOptions.ReadAgentCredential(Parse(), AgentKind.Claude, "secret"));
    }

    [TestMethod]
    public void ReadAgentCredential_UsesExplicitValue_WhenValid()
    => Assert.AreEqual
    (
        new AgentCredential("OPENAI_API_KEY", "secret"),
        JobOptions.ReadAgentCredential(Parse("--agent-api-key-env", "OPENAI_API_KEY"), AgentKind.OpenCode, "secret")
    );

    [TestMethod]
    public void ReadAgentCredential_RejectsPiAgent_WithoutOverride()
    {
        // The composed line, because that is what the user is shown and the half either side of
        // the colon is chosen to read as one sentence with the other.
        var error = ErrorOf(() => JobOptions.ReadAgentCredential(Parse(), AgentKind.Pi, "secret"));
        Assert.AreEqual("--agent-api-key-env: is required when agent=pi and agent-api-key is set", error);
    }

    [TestMethod]
    [DataRow("NOT_A_CREDENTIAL")]
    // Full rejection matrix (RIX_*, AGENT_API_KEY*, GITHUB_*) is covered by
    // AgentCredentialTests; this just proves the reader wires the error through.
    [DataRow("RIX_AGENT")]
    public void ReadAgentCredential_RejectsName_ThatIsNotCredentialShaped(string envName)
    {
        var error = ErrorOf(() => JobOptions.ReadAgentCredential(Parse("--agent-api-key-env", envName), AgentKind.OpenCode, "secret"));
        StringAssert.StartsWith(error, "--agent-api-key-env: ");
        StringAssert.Contains(error, envName);
    }
}
