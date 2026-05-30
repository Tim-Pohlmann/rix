using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rix.Process;

namespace Rix.Repository;

internal sealed class GitHubRepositoryHost : IRepositoryHost
{
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _readToken;
    private readonly string _writeToken;
    private readonly HttpClient _http;

    internal GitHubRepositoryHost(RepoIdentifier repo, ReadToken readToken, WriteToken writeToken)
        : this(repo, readToken, writeToken, handler: null) { }

    private GitHubRepositoryHost(RepoIdentifier repo, ReadToken readToken, WriteToken writeToken, HttpMessageHandler? handler)
    {
        _owner = repo.Owner;
        _repo = repo.Name;
        _readToken = readToken.Value;
        _writeToken = writeToken.Value;
        _http = BuildHttpClient(readToken.Value, handler);
    }

    internal static GitHubRepositoryHost WithHandler(
        RepoIdentifier repo,
        ReadToken readToken,
        WriteToken writeToken,
        HttpMessageHandler handler) =>
        new(repo, readToken, writeToken, handler);

    private static HttpClient BuildHttpClient(string token, HttpMessageHandler? handler)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("rix/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    {
        var cloneUrl = $"https://x-access-token:{_readToken}@github.com/{_owner}/{_repo}.git";
        return RunGitAsync(["clone", cloneUrl, targetDirectory], cancellationToken);
    }

    public async Task<bool> BranchExistsOnRemoteAsync(string branch, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/branches/{Uri.EscapeDataString(branch)}";
        var response = await _http.GetAsync(url, cancellationToken);
        return response.StatusCode == System.Net.HttpStatusCode.OK;
    }

    public Task PushBranchAsync(string branch, CancellationToken cancellationToken)
    {
        var remoteUrl = $"https://x-access-token:{_writeToken}@github.com/{_owner}/{_repo}.git";
        return RunGitAsync(["push", remoteUrl, $"refs/heads/{branch}:refs/heads/{branch}"], cancellationToken);
    }

    public async Task<string> CreatePullRequestAsync(
        string branch,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/pulls";
        var payload = JsonSerializer.Serialize(
            new CreatePrRequestDto(Title: title, Head: branch, Base: "main", Body: body),
            GitHubJsonContext.Default.CreatePrRequestDto);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _writeToken);

        var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub PR creation failed ({(int)response.StatusCode}): {json}");

        var pr = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.CreatePrResponseDto)
            ?? throw new InvalidOperationException("GitHub returned null PR response");

        return pr.HtmlUrl;
    }

    private static async Task RunGitAsync(string[] args, CancellationToken cancellationToken)
    {
        var result = await ProcessWrapper.RunAsync(
            "git", args,
            workingDirectory: Path.GetTempPath(),
            environment: ProcessWrapper.BuildSanitizedEnvironment(),
            cancellationToken: cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException($"git {args[0]} failed with exit code {result.ExitCode}");
    }

    internal record CreatePrRequestDto(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("head")] string Head,
        [property: JsonPropertyName("base")] string Base,
        [property: JsonPropertyName("body")] string Body);

    internal record CreatePrResponseDto(
        [property: JsonPropertyName("html_url")] string HtmlUrl);
}

[JsonSerializable(typeof(GitHubRepositoryHost.CreatePrRequestDto))]
[JsonSerializable(typeof(GitHubRepositoryHost.CreatePrResponseDto))]
internal partial class GitHubJsonContext : JsonSerializerContext { }
