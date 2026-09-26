using Rix.Agents;
using Rix.CiFailure;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

internal sealed class StubCiHost(
    Func<RunId, Task<CiRun>>? getRun = null,
    Func<RunId, Task<string>>? getLogs = null) : ICiHost
{
    /// <summary>The log budget the caller asked for, so a test can assert the cap is actually
    /// pushed down to the host rather than only applied afterwards.</summary>
    internal int? TotalTailChars { get; private set; }

    public Task<CiRun> GetRunAsync(RunId runId, CancellationToken cancellationToken)
    => getRun switch { { } check => check(runId), _ => throw new InvalidOperationException("getRun not stubbed") };

    public Task<string> GetFailedJobLogsAsync(RunId runId, int totalTailChars, CancellationToken cancellationToken)
    {
        TotalTailChars = totalTailChars;
        return getLogs switch { { } check => check(runId), _ => Task.FromResult("") };
    }
}

internal sealed class StubCiFailureRepoHost(
    Func<BranchName, Task<int?>>? findPr = null,
    Func<BranchName, Task<int>>? countRixCommits = null) : ICiFailureRepoHost
{
    /// <summary>The cap the loop guard was asked to count against, so a test can assert the
    /// configured value reaches the host instead of a constant fixed in the detector.</summary>
    internal MaxRixCommits? MaxRixCommits { get; private set; }

    public Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken)
    => findPr switch { { } check => check(branch), _ => Task.FromResult<int?>(null) };

    public Task<int> CountLeadingRixCommitsAsync(BranchName branch, MaxRixCommits max, CancellationToken cancellationToken)
    {
        MaxRixCommits = max;
        return countRixCommits switch { { } count => count(branch), _ => Task.FromResult(0) };
    }
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

/// <summary>The CI run most ci-failure tests describe: one that ran, on a branch of the repo
/// itself, with a title and URL. Only <paramref name="outcome"/>, <paramref name="branch"/> and
/// <paramref name="headRepo"/> vary between scenarios, so the rest is fixed here rather than
/// restated per test.</summary>
internal static class TestRuns
{
    internal static CiRun Sample(CiOutcome outcome, string branch = "rix/fix", string headRepo = "owner/repo")
    => new(outcome, "Fix thing", "https://github.com/owner/repo/actions/runs/1", new BranchName(branch), new RepoIdentifier(headRepo));
}

internal sealed class StubGit(
    Func<BranchName, Task<bool>>? branchExists = null,
    Func<string, Task>? createBundle = null,
    Func<Task>? clone = null,
    Func<BranchName, Task<bool>>? branchExistsLocally = null,
    Func<string, Task>? configureIdentity = null,
    Func<BranchName, Task>? pushBranch = null,
    Func<string, SubDirectoryPath, Task>? sparseClone = null) : IGit
{
    public List<BranchName> PushedBranches { get; } = [];
    public bool CloneCalled { get; private set; }

    /// <summary>Succeeds by default; override via the <c>clone</c> constructor parameter to
    /// simulate a git clone failure (e.g. throwing <see cref="RepoHostException"/>, as the
    /// real <see cref="GitCli.CloneAsync"/> does).</summary>
    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    {
        CloneCalled = true;
        return clone switch { { } check => check(), _ => Task.CompletedTask };
    }

    /// <summary>Succeeds without creating anything by default; override via the
    /// <c>sparseClone</c> constructor parameter to lay out the checkout or to simulate a failure.</summary>
    public Task SparseCloneAsync(string targetDirectory, SubDirectoryPath directory, CancellationToken cancellationToken)
    => sparseClone switch { { } check => check(targetDirectory, directory), _ => Task.CompletedTask };

    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    => branchExists switch { { } check => check(branch), _ => Task.FromResult(false) };

    /// <summary>Exists by default, since most tests care about simulating the agent's own
    /// process/git behaviour rather than this guard; override via <c>branchExistsLocally</c> to
    /// simulate an agent that queued a PR for a branch it never actually committed.</summary>
    public Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    => branchExistsLocally switch { { } check => check(branch), _ => Task.FromResult(true) };

    /// <summary>Succeeds by default; override via the <c>configureIdentity</c> constructor
    /// parameter to see the directory it runs in or to simulate a git identity configuration
    /// failure.</summary>
    public Task ConfigureIdentityAsync(string repoDirectory, CancellationToken cancellationToken)
    => configureIdentity switch { { } check => check(repoDirectory), _ => Task.CompletedTask };

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

    public Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    {
        PushedBranches.Add(branch);
        return pushBranch switch { { } check => check(branch), _ => Task.CompletedTask };
    }
}

internal sealed class StubSubmitRepoHost(Func<PendingPr, Task<string>>? createPullRequest = null) : ISubmitRepoHost
{
    public List<PendingPr> CreatedPrs { get; } = [];

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

/// <summary>Records each fetch so tests can assert the agent home files were requested with the
/// configured repo and path. <c>onFetch</c> gets the checkout dir and returns the directory to merge
/// into the home, or throws <see cref="Rix.Repository.RepoHostException"/> to simulate a fetch
/// failure; by default the empty checkout dir itself is returned, so nothing is copied.</summary>
internal sealed class StubAgentHomeFetcher(Func<DirectoryPath, DirectoryPath>? onFetch = null) : IAgentHomeFetcher
{
    public List<(RepoIdentifier Repo, SubDirectoryPath SourcePath)> Fetches { get; } = [];

    public Task<DirectoryPath> FetchAsync
    (
        RepoIdentifier repo, SubDirectoryPath sourcePath, DirectoryPath checkoutDir, CancellationToken cancellationToken
    )
    {
        Fetches.Add((repo, sourcePath));
        return Task.FromResult(onFetch?.Invoke(checkoutDir) ?? checkoutDir);
    }
}

/// <summary>The local disk, except for the operations a test overrides — to make a write or a
/// cleanup fail the way a full disk or a locked file would, or to decide what exists, without
/// arranging it on disk.</summary>
internal sealed class StubFileSystem(
    Func<string, Task>? writeAllText = null,
    Action<string>? deleteDirectory = null,
    Func<string, bool>? directoryExists = null) : IFileSystem
{
    private readonly LocalFileSystem _real = new();

    public bool FileExists(string path) => _real.FileExists(path);

    public bool DirectoryExists(string path)
    => directoryExists switch
    {
        { } exists => exists(path),
        _ => _real.DirectoryExists(path),
    };

    public bool PathExists(string path) => _real.PathExists(path);

    public void CreateDirectory(string path) => _real.CreateDirectory(path);

    public void DeleteDirectory(string path)
    {
        if (deleteDirectory is { } delete)
            delete(path);
        else
            _real.DeleteDirectory(path);
    }

    public IEnumerable<FileSystemEntry> EnumerateEntries(string directory) => _real.EnumerateEntries(directory);

    public void CopyFile(string source, string destination) => _real.CopyFile(source, destination);

    public void CreateSymbolicLink(string path, string target) => _real.CreateSymbolicLink(path, target);

    public Stream OpenRead(string path) => _real.OpenRead(path);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    => writeAllText switch
    {
        { } write => write(path),
        _ => _real.WriteAllTextAsync(path, content, cancellationToken),
    };
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
