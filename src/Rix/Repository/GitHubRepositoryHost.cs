using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rix.Process;

namespace Rix.Repository;

internal sealed class GitHubRepositoryHost : IRepositoryHost, ISubmitHost
{
    private readonly RepoIdentifier _repo;
    private readonly string _credential;
    private readonly HttpClient _http;
    private readonly RunProcessAsync _runProcess;

    internal GitHubRepositoryHost(RepoIdentifier repo, ReadToken readToken, RunProcessAsync runProcess)
        : this(repo, readToken.Value, runProcess, handler: null) { }

    internal GitHubRepositoryHost(
        RepoIdentifier repo,
        ReadToken readToken,
        RunProcessAsync runProcess,
        HttpMessageHandler? handler)
        : this(repo, readToken.Value, runProcess, handler) { }

    internal GitHubRepositoryHost(RepoIdentifier repo, WriteToken writeToken, RunProcessAsync runProcess)
        : this(repo, writeToken.Value, runProcess, handler: null) { }

    internal GitHubRepositoryHost(
        RepoIdentifier repo,
        WriteToken writeToken,
        RunProcessAsync runProcess,
        HttpMessageHandler? handler)
        : this(repo, writeToken.Value, runProcess, handler) { }

    private GitHubRepositoryHost(
        RepoIdentifier repo,
        string credential,
        RunProcessAsync runProcess,
        HttpMessageHandler? handler)
    {
        _repo = repo;
        _credential = credential;
        _http = BuildHttpClient(credential, handler);
        _runProcess = runProcess;
    }

    private static HttpClient BuildHttpClient(string token, HttpMessageHandler? handler)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("rix/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) =>
        RunGitAsync(["clone", $"https://x-access-token:{_credential}@github.com/{_repo.Value}.git", targetDirectory],
            workingDirectory: Path.GetTempPath(), cancellationToken);

    public Task CreateBundleAsync(
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken) =>
        RunGitAsync(["bundle", "create", bundlePath, $"{baseBranch.Value}..{branch.Value}"],
            workingDirectory: repoDirectory, cancellationToken);

    public async Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_repo.Value}/branches/{Uri.EscapeDataString(branch.Value)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_repo.Value}/pulls";
        var request = new CreatePullRequestRequest(
            Title: pullRequest.Title.Value,
            Head: pullRequest.Branch.Value,
            Base: pullRequest.BaseBranch.Value,
            Body: pullRequest.Body.Value);
        var json = JsonSerializer.Serialize(request, GitHubApiJsonContext.Default.CreatePullRequestRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task RunGitAsync(string[] args, string workingDirectory, CancellationToken cancellationToken)
    {
        // No environment overrides: the subprocess already inherits the full parent environment.
        // Forcing PATH/HOME here would be redundant and, on Windows (where HOME is usually unset),
        // would inject an empty HOME that disrupts git's home-directory resolution.
        var result = await _runProcess("git", args, workingDirectory, null, null, cancellationToken);
        if (result is ProcessFailure f)
            throw new InvalidOperationException($"git {args[0]} failed: {f.Reason}");
    }
}

/// <summary>The JSON body of a GitHub "create a pull request" REST call.</summary>
internal sealed record CreatePullRequestRequest(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("head")] string Head,
    [property: JsonPropertyName("base")] string Base,
    [property: JsonPropertyName("body")] string Body);

[JsonSerializable(typeof(CreatePullRequestRequest))]
internal partial class GitHubApiJsonContext : JsonSerializerContext { }
