using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Rix.Process;

namespace Rix.Repository;

/// <summary>A single git invocation: its <paramref name="Args"/> and the
/// <paramref name="WorkingDirectory"/> it runs in (a neutral temp dir for clone, the clone itself
/// for bundle).</summary>
internal sealed record GitCommand(string[] Args, string WorkingDirectory);

internal sealed class GitHubRepositoryHost : IRepositoryHost
{
    private readonly RepoIdentifier _repo;
    private readonly ReadToken _readToken;
    private readonly HttpClient _http;
    private readonly Func<GitCommand, CancellationToken, Task<ProcessResult>> _gitRunner;

    internal GitHubRepositoryHost(RepoIdentifier repo, ReadToken readToken)
        : this(repo, readToken, handler: null, gitRunner: null) { }

    internal GitHubRepositoryHost(
        RepoIdentifier repo,
        ReadToken readToken,
        HttpMessageHandler? handler,
        Func<GitCommand, CancellationToken, Task<ProcessResult>>? gitRunner)
    {
        _repo = repo;
        _readToken = readToken;
        _http = BuildHttpClient(readToken, handler);
        _gitRunner = gitRunner ?? DefaultGitRunner;
    }

    private static HttpClient BuildHttpClient(ReadToken token, HttpMessageHandler? handler)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("rix/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) =>
        RunGitAsync(
            new GitCommand(
                ["clone", $"https://x-access-token:{_readToken.Value}@github.com/{_repo.Value}.git", targetDirectory],
                WorkingDirectory: Path.GetTempPath()),
            cancellationToken);

    public Task CreateBundleAsync(
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken) =>
        RunGitAsync(
            new GitCommand(
                ["bundle", "create", bundlePath, $"{baseBranch.Value}..{branch.Value}"],
                WorkingDirectory: repoDirectory),
            cancellationToken);

    public async Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_repo.Value}/branches/{Uri.EscapeDataString(branch.Value)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    private async Task RunGitAsync(GitCommand command, CancellationToken cancellationToken)
    {
        var result = await _gitRunner(command, cancellationToken);
        if (result is ProcessFailure f)
            throw new InvalidOperationException($"git {command.Args[0]} failed: {f.Reason}");
    }

    [ExcludeFromCodeCoverage]
    private static Task<ProcessResult> DefaultGitRunner(GitCommand command, CancellationToken cancellationToken) =>
        ProcessWrapper.RunAsync(
            "git", command.Args,
            workingDirectory: command.WorkingDirectory,
            environmentOverrides: GitEnvironment.Current,
            cancellationToken: cancellationToken);
}
