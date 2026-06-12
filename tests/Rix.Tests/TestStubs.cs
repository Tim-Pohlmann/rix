using Rix.Agents;
using Rix.FileSystem;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

/// <summary>An <see cref="IFileSystem"/> that records the directories it was asked to create and
/// delete instead of touching disk — lets tests assert the clone/cleanup lifecycle.</summary>
internal sealed class RecordingFileSystem : IFileSystem
{
    public List<string> Created { get; } = [];
    public List<string> Deleted { get; } = [];

    public void CreateDirectory(string path) => Created.Add(path);
    public void DeleteDirectory(string path) => Deleted.Add(path);
}

internal sealed class StubRepositoryHost(Func<BranchName, Task<bool>>? branchExists = null) : IRepositoryHost
{
    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) =>
        Task.CompletedTask;
    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken) =>
        branchExists is not null ? branchExists(branch) : Task.FromResult(false);
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
