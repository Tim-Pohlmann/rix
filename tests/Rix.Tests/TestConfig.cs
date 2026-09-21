using Rix.Agents;
using Rix.CiFailure;
using Rix.Initialize;
using Rix.Job;
using Rix.Submit;

namespace Rix.Tests;

/// <summary>Test helpers for building strongly-typed configs from a few overridable primitives,
/// so each test names only the values it cares about. Failing here (an
/// <see cref="InvalidInputException"/> from a value object) means the test fixture itself is
/// malformed.</summary>
internal static class TestConfig
{
    internal static JobConfig Valid
    (
        string repo = "owner/repo",
        string prompt = "do it",
        string readToken = "tok",
        int maxTokens = JobConfig.DefaultMaxTokens,
        int timeoutMinutes = JobConfig.DefaultTimeoutMinutes,
        string? workDir = null,
        string? outputDir = null,
        AgentKind agent = JobConfig.DefaultAgent,
        string? model = null,
        string? agentApiKey = null,
        string? agentApiKeyEnv = null,
        IReadOnlyList<BranchName>? allowedPushBranches = null,
        FactoryContextConfig? factoryContext = null
    )
    {
        // The same call the CLI makes, so a change to when the credential is required reaches
        // the fixtures too instead of leaving them asserting a rule the CLI no longer follows.
        var credential = AgentCredential.Resolve(agent, agentApiKey, agentApiKeyEnv);
        return new JobConfig
        (
            new RepoIdentifier(repo),
            new GitReadToken(readToken),
            new TimeoutMinutes(timeoutMinutes),
            WorkDir: new DirectoryPath(workDir ?? Path.GetTempPath()),
            OutputDir: new DirectoryPath(outputDir ?? Path.GetTempPath()),
            new AgentConfig(agent, prompt, new MaxTokens(maxTokens), model, credential),
            allowedPushBranches ?? [],
            factoryContext
        );
    }

    internal static SubmitConfig ValidSubmit
    (
        string repo = "owner/repo",
        string writeToken = "tok",
        string? inputDir = null,
        string? workDir = null,
        string? allowedPushBranches = null
    )
    => new
    (
        new RepoIdentifier(repo),
        new GitToken(writeToken),
        InputDir: new DirectoryPath(inputDir ?? Path.GetTempPath()),
        WorkDir: new DirectoryPath(workDir ?? Path.GetTempPath()),
        BranchName.ParseAllowList(allowedPushBranches)
    );

    internal static CiFailureConfig ValidCiFailure
    (
        string repo = "owner/repo",
        string readToken = "read-tok",
        long runId = 1,
        string? workDir = null,
        string? outputDir = null,
        int maxRixCommits = CiFailureConfig.DefaultMaxRixCommits,
        AgentKind agent = JobConfig.DefaultAgent
    )
    => new
    (
        new RunId(runId),
        new RepoIdentifier(repo),
        new GitReadToken(readToken),
        new TimeoutMinutes(JobConfig.DefaultTimeoutMinutes),
        WorkDir: new DirectoryPath(workDir ?? Path.GetTempPath()),
        OutputDir: new DirectoryPath(outputDir ?? Path.GetTempPath()),
        agent,
        new MaxTokens(JobConfig.DefaultMaxTokens),
        new MaxRixCommits(maxRixCommits)
    );

    internal static InitializeConfig ValidInitialize(string? dir = null, string? workflowRef = null)
    => new(new DirectoryPath(dir ?? Path.GetTempPath()), new WorkflowRef(workflowRef ?? "v0"));
}
