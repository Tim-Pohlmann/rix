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
        IReadOnlyList<BranchName>? allowedPushBranches = null
    )
    {
        // The same call the CLI makes, so a change to when the name is required reaches the
        // fixtures too instead of leaving them asserting a rule the CLI no longer follows.
        var apiKeyEnv = AgentCredential.ResolveEnvNameOrNull(agent, agentApiKey, agentApiKeyEnv);
        return new JobConfig
        (
            Repo: new RepoIdentifier(repo),
            ReadToken: new GitReadToken(readToken),
            TimeoutMinutes: new TimeoutMinutes(timeoutMinutes),
            WorkDir: new DirectoryPath(workDir ?? Path.GetTempPath()),
            OutputDir: new DirectoryPath(outputDir ?? Path.GetTempPath()),
            Agent: new AgentConfig(agent, prompt, new MaxTokens(maxTokens), model, agentApiKey, apiKeyEnv),
            AllowedPushBranches: allowedPushBranches ?? []
        );
    }

    internal static SubmitConfig ValidSubmit
    (
        string repo = "owner/repo",
        string writeToken = "tok",
        string? inputDir = null,
        string? workDir = null
    )
    => new
    (
        Repo: new RepoIdentifier(repo),
        WriteToken: new GitToken(writeToken),
        InputDir: new DirectoryPath(inputDir ?? Path.GetTempPath()),
        WorkDir: new DirectoryPath(workDir ?? Path.GetTempPath())
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
        RunId: new RunId(runId),
        Repo: new RepoIdentifier(repo),
        ReadToken: new GitReadToken(readToken),
        TimeoutMinutes: new TimeoutMinutes(JobConfig.DefaultTimeoutMinutes),
        WorkDir: new DirectoryPath(workDir ?? Path.GetTempPath()),
        OutputDir: new DirectoryPath(outputDir ?? Path.GetTempPath()),
        Agent: agent,
        MaxTokens: new MaxTokens(JobConfig.DefaultMaxTokens),
        MaxRixCommits: new MaxRixCommits(maxRixCommits)
    );

    internal static InitializeConfig ValidInitialize(string? dir = null, string? workflowRef = null)
    => new(new DirectoryPath(dir ?? Path.GetTempPath()), new WorkflowRef(workflowRef ?? "v0"));
}
