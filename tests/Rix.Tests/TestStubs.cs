using Rix.Agents;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

internal sealed class StubRepositoryHost(
    Func<BranchName, Task<bool>>? branchExists = null,
    Func<string, Task>? createBundle = null,
    Func<Task>? clone = null,
    Func<BranchName, Task<bool>>? branchExistsLocally = null,
    Func<Task>? configureGit = null) : IRepositoryReadHost
{
    /// <summary>Succeeds by default; override via the <c>clone</c> constructor parameter to
    /// simulate a git clone failure (e.g. throwing <see cref="InvalidOperationException"/>, as the
    /// real <see cref="GitHubReadHost.CloneAsync"/> does).</summary>
    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    => clone switch { { } check => check(), _ => Task.CompletedTask };
    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    => branchExists switch { { } check => check(branch), _ => Task.FromResult(false) };

    /// <summary>Exists by default, since most tests care about simulating the agent's own
    /// process/git behaviour rather than this guard; override via <c>branchExistsLocally</c> to
    /// simulate an agent that queued a PR for a branch it never actually committed.</summary>
    public Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    => branchExistsLocally switch { { } check => check(branch), _ => Task.FromResult(true) };

    /// <summary>Succeeds by default; override via the <c>configureGit</c> constructor parameter to
    /// simulate a git identity configuration failure.</summary>
    public Task ConfigureGitAsync(string repoDirectory, CancellationToken cancellationToken)
    => configureGit switch { { } check => check(), _ => Task.CompletedTask };

    /// <summary>By default writes a placeholder bundle file so callers that inspect the output
    /// directory see it; override via the <c>createBundle</c> constructor parameter to simulate
    /// git failures.</summary>
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
    Func<PendingPr, Task<string>>? createPullRequest = null,
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

    /// <summary>The submit flow never bundles, so this guard is irrelevant to it; always reports the
    /// branch as present.</summary>
    public Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    => Task.FromResult(true);

    /// <summary>The submit flow pushes already-made commits, so it has no need to configure an
    /// identity for new ones.</summary>
    public Task ConfigureGitAsync(string repoDirectory, CancellationToken cancellationToken)
    => Task.CompletedTask;

    public Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    {
        PushedBranches.Add(branch);
        return pushBranch switch { { } check => check(branch), _ => Task.CompletedTask };
    }

    public Task<string> CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken)
    {
        CreatedPrs.Add(pullRequest);
        return createPullRequest switch
        {
            { } check => check(pullRequest),
            _ => Task.FromResult($"https://github.com/owner/repo/pull/{CreatedPrs.Count}"),
        };
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
