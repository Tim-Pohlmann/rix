using Rix.Agents;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

internal sealed class StubRepositoryHost(Func<BranchName, Task<bool>>? branchExists = null) : IRepositoryHost
{
    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken) =>
        branchExists is not null ? branchExists(branch) : Task.FromResult(false);
}

internal sealed class StubSubmitHost(
    Func<BranchName, Task<bool>>? branchExists = null,
    Func<PendingPr, Task>? createPullRequest = null) : ISubmitHost
{
    public List<PendingPr> CreatedPrs { get; } = [];
    public bool CloneCalled { get; private set; }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    {
        CloneCalled = true;
        return Task.CompletedTask;
    }

    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken) =>
        branchExists is not null ? branchExists(branch) : Task.FromResult(false);

    public Task CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken)
    {
        CreatedPrs.Add(pullRequest);
        return createPullRequest is not null ? createPullRequest(pullRequest) : Task.CompletedTask;
    }
}

/// <summary>
/// A coding agent for tests: install behavior is supplied by the caller, while invocation
/// and cost parsing delegate to the real <see cref="ClaudeAgent"/> so tests exercise the
/// genuine "claude" argument layout and NDJSON cost format.
/// </summary>
internal sealed class StubAgent(Func<CancellationToken, Task<InstallResult>> install) : ICodingAgent
{
    private readonly ClaudeAgent _real = new();

    public Task<InstallResult> EnsureInstalledAsync(RunProcessAsync _, CancellationToken cancellationToken) =>
        install(cancellationToken);

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt) =>
        _real.BuildInvocation(config, systemPrompt);

    public decimal? ParseCost(string outputLine) => _real.ParseCost(outputLine);
}
