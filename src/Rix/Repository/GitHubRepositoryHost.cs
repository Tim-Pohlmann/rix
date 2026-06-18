using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Rix.Process;

namespace Rix.Repository;

internal sealed class GitHubRepositoryHost : IRepositoryHost, ISubmitHost
{
    private readonly RepoIdentifier _repo;
    private readonly HttpClient _http;
    private readonly RunProcessAsync _runProcess;
    private readonly IReadOnlyDictionary<string, string> _gitAuthEnv;

    internal GitHubRepositoryHost(
        RepoIdentifier repo, ReadToken readToken, RunProcessAsync runProcess, HttpMessageHandler? handler = null)
        : this(repo, readToken.Value, runProcess, handler) { }

    internal GitHubRepositoryHost(
        RepoIdentifier repo, WriteToken writeToken, RunProcessAsync runProcess, HttpMessageHandler? handler = null)
        : this(repo, writeToken.Value, runProcess, handler) { }

    private GitHubRepositoryHost(
        RepoIdentifier repo,
        string credential,
        RunProcessAsync runProcess,
        HttpMessageHandler? handler)
    {
        _repo = repo;
        _http = BuildHttpClient(credential, handler);
        _runProcess = runProcess;
        _gitAuthEnv = BuildGitAuthEnv(credential);
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

    /// <summary>
    /// Builds the environment that authenticates git over HTTPS without ever placing the token in
    /// a command-line argument (visible via <c>ps</c>) or in the cloned repo's persisted
    /// <c>.git/config</c> remote URL. Git reads these <c>GIT_CONFIG_*</c> variables as ad-hoc config,
    /// so the credential lives only in this process's environment for the duration of each call.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildGitAuthEnv(string token)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{token}"));
        return new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader",
            ["GIT_CONFIG_VALUE_0"] = $"Authorization: Basic {basic}",
        };
    }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken) =>
        RunGitAsync(["clone", $"https://github.com/{_repo.Value}.git", targetDirectory],
            workingDirectory: Path.GetTempPath(), cancellationToken);

    public Task CreateBundleAsync(
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken) =>
        RunGitAsync(["bundle", "create", bundlePath, $"{baseBranch.Value}..{branch.Value}"],
            workingDirectory: repoDirectory, cancellationToken);

    public Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken) =>
        RunGitAsync(["push", "origin", branch.Value], workingDirectory: repoDirectory, cancellationToken);

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
        using var content = JsonContent.Create(request, GitHubApiJsonContext.Default.CreatePullRequestRequest);
        using var response = await _http.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task RunGitAsync(string[] args, string workingDirectory, CancellationToken cancellationToken)
    {
        // Pass only the GIT_CONFIG_* auth variables as overrides; the subprocess still inherits the
        // full parent environment (PATH, HOME, ...) on top of these, so we never force those here.
        var result = await _runProcess("git", args, workingDirectory, _gitAuthEnv, null, cancellationToken);
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
