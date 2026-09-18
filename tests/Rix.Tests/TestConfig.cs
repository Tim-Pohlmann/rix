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
    internal static JobSettings ValidSettings
    (
        string repo = "owner/repo",
        string readToken = "tok",
        int maxTokens = JobConfig.DefaultMaxTokens,
        int timeoutMinutes = JobConfig.DefaultTimeoutMinutes,
        string? workDir = null,
        string? outputDir = null,
        AgentKind agent = JobConfig.DefaultAgent,
        string? model = null,
        string? agentApiKey = null,
        string? agentApiKeyEnv = null
    )
    {
        // Mirrors the CLI: the env var name is only resolved once there is a key to export.
        var apiKeyEnv = agentApiKey switch
        {
            null => null,
            _ => AgentCredential.ResolveEnvName(agent, agentApiKeyEnv),
        };
        return new JobSettings
        (
            Repo: new RepoIdentifier(repo),
            ReadToken: new GitReadToken(readToken),
            TimeoutMinutes: new TimeoutMinutes(timeoutMinutes),
            WorkDir: new DirectoryPath(workDir ?? Path.GetTempPath()),
            OutputDir: new DirectoryPath(outputDir ?? Path.GetTempPath()),
            Agent: agent,
            MaxTokens: new MaxTokens(maxTokens),
            Model: model,
            ApiKey: agentApiKey,
            ApiKeyEnv: apiKeyEnv
        );
    }

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
    => ValidSettings
    (
        repo: repo,
        readToken: readToken,
        maxTokens: maxTokens,
        timeoutMinutes: timeoutMinutes,
        workDir: workDir,
        outputDir: outputDir,
        agent: agent,
        model: model,
        agentApiKey: agentApiKey,
        agentApiKeyEnv: agentApiKeyEnv
    ).ToJob(prompt, allowedPushBranches ?? []);

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
        string? outputDir = null
    )
    => new(new RunId(runId), ValidSettings(repo: repo, readToken: readToken, workDir: workDir, outputDir: outputDir));

    internal static InitializeConfig ValidInitialize(string? dir = null)
    => new(new DirectoryPath(dir ?? Path.GetTempPath()));
}
