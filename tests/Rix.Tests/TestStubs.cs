using Rix.Agents;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

internal sealed class StubRepositoryHost(
    Func<BranchName, Task<bool>>? branchExists = null,
    Func<string, Task>? createBundle = null,
    Func<Task>? clone = null) : IRepositoryReadHost
{
    /// <summary>Succeeds by default; override via <paramref name="clone"/> to simulate a git
    /// clone failure (e.g. throwing <see cref="InvalidOperationException"/>, as the real
    /// <see cref="Repository.GitHubReadHost.CloneAsync"/> does).</summary>
    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    => clone switch { { } check => check(), _ => Task.CompletedTask };
    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    => branchExists switch { { } check => check(branch), _ => Task.FromResult(false) };

    /// <summary>By default writes a placeholder bundle file so callers that inspect the output
    /// directory see it; override via <paramref name="createBundle"/> to simulate git failures.</summary>
    public Task CreateBundleAsync(
        string repoDirectory, string bundlePath, BranchName baseBranch, BranchName branch, CancellationToken cancellationToken)
    => createBundle switch
    {
        { } check => check(bundlePath),
        _ => File.WriteAllTextAsync(bundlePath, "fake-bundle", cancellationToken),
    };
}

internal sealed class StubSubmitHost(
    Func<BranchName, Task<bool>>? branchExists = null,
    Func<PendingPr, Task>? createPullRequest = null,
    Func<BranchName, Task>? pushBranch = null) : IRepositoryHost
{
    public List<PendingPr> CreatedPrs { get; } = [];
    public List<BranchName> PushedBranches { get; } = [];
    public bool CloneCalled { get; private set; }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    {
        CloneCalled = true;
        return Task.CompletedTask;
    }

    /// <summary>The submit flow never bundles (that is the job path's job), so this should be unreachable.</summary>
    public Task CreateBundleAsync(
        string repoDirectory, string bundlePath, BranchName baseBranch, BranchName branch, CancellationToken cancellationToken)
    => throw new NotSupportedException("submit flow does not create bundles");

    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    => branchExists switch { { } check => check(branch), _ => Task.FromResult(false) };

    public Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    {
        PushedBranches.Add(branch);
        return pushBranch switch { { } check => check(branch), _ => Task.CompletedTask };
    }

    public Task CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken)
    {
        CreatedPrs.Add(pullRequest);
        return createPullRequest switch { { } check => check(pullRequest), _ => Task.CompletedTask };
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

    public Task<InstallResult> EnsureInstalledAsync(RunProcessAsync _, CancellationToken cancellationToken)
    => install(cancellationToken);

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt)
    => _real.BuildInvocation(config, systemPrompt);

    public decimal? ParseCost(string outputLine) => _real.ParseCost(outputLine);
}
