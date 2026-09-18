using Rix.Agents;
using Rix.CiFailure;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

internal sealed class StubCiFailureHost(
    Func<RunId, Task<WorkflowRun>>? getRun = null,
    Func<RunId, Task<string>>? getLogs = null,
    Func<BranchName, Task<int?>>? findPr = null) : IGitHubCiFailureHost
{
    /// <summary>The log budget the caller asked for, so a test can assert the cap is actually
    /// pushed down to the host rather than only applied afterwards.</summary>
    internal int? TotalTailChars { get; private set; }

    public Task<WorkflowRun> GetRunAsync(RunId runId, CancellationToken cancellationToken)
    => getRun switch { { } check => check(runId), _ => throw new InvalidOperationException("getRun not stubbed") };

    public Task<string> GetFailedJobLogsAsync(RunId runId, int totalTailChars, CancellationToken cancellationToken)
    {
        TotalTailChars = totalTailChars;
        return getLogs switch { { } check => check(runId), _ => Task.FromResult("") };
    }

    public Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken)
    => findPr switch { { } check => check(branch), _ => Task.FromResult<int?>(null) };
}

/// <summary>An <see cref="HttpMessageHandler"/> that answers every request from
/// <paramref name="handler"/>, for tests that drive a real host against canned GitHub API
/// responses.</summary>
internal sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    => Task.FromResult(handler(request));
}

/// <summary>A response body that fails once it is already being read, for the case a bad status
/// can't stand in for: the request went out, headers came back, and the connection dropped
/// mid-stream.</summary>
internal sealed class FailingStream : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => 0;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    => throw new IOException("connection reset by peer");

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>The workflow run most ci-failure tests describe: one that ran, on a branch, with a
/// title and URL. Only <paramref name="conclusion"/> and <paramref name="branch"/> vary between
/// scenarios, so the rest is fixed here rather than restated per test.</summary>
internal static class TestRuns
{
    internal static WorkflowRun Sample(string? conclusion, string branch = "rix/fix")
    => new(conclusion, "Fix thing", "https://github.com/owner/repo/actions/runs/1", branch);
}

internal sealed class StubRepositoryHost(
    Func<BranchName, Task<bool>>? branchExists = null,
    Func<string, Task>? createBundle = null,
    Func<Task>? clone = null,
    Func<BranchName, Task<bool>>? branchExistsLocally = null,
    Func<Task>? configureGit = null) : IRepositoryReadHost
{
    /// <summary>Succeeds by default; override via the <c>clone</c> constructor parameter to
    /// simulate a git clone failure (e.g. throwing <see cref="RepositoryHostException"/>, as the
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

    public string? ParseTranscriptLine(string outputLine) => _real.ParseTranscriptLine(outputLine);
}
