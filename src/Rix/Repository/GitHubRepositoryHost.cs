using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rix.Process;

namespace Rix.Repository;

internal sealed class GitHubRepositoryHost : IRepositoryHost
{
    private readonly RepoIdentifier _repo;
    private readonly ReadToken _readToken;
    private readonly WriteToken _writeToken;
    private readonly HttpClient _http;
    private readonly Func<string[], CancellationToken, Task<ProcessResult>> _gitRunner;

    internal GitHubRepositoryHost(RepoIdentifier repo, ReadToken readToken, WriteToken writeToken)
        : this(repo, readToken, writeToken, handler: null, gitRunner: null) { }

    private GitHubRepositoryHost(
        RepoIdentifier repo,
        ReadToken readToken,
        WriteToken writeToken,
        HttpMessageHandler? handler,
        Func<string[], CancellationToken, Task<ProcessResult>>? gitRunner)
    {
        _repo = repo;
        _readToken = readToken;
        _writeToken = writeToken;
        _http = BuildHttpClient(readToken, handler);
        _gitRunner = gitRunner ?? DefaultGitRunner;
    }

    internal static GitHubRepositoryHost WithHandler(
        RepoIdentifier repo,
        ReadToken readToken,
        WriteToken writeToken,
        HttpMessageHandler handler,
        Func<string[], CancellationToken, Task<ProcessResult>>? gitRunner = null) =>
        new(repo, readToken, writeToken, handler, gitRunner);

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
        RunGitWithCredentialsAsync(
            _readToken.Value,
            ["clone", $"https://github.com/{_repo.Value}.git", targetDirectory],
            cancellationToken);

    public async Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_repo.Value}/branches/{Uri.EscapeDataString(branch.Value)}";
        var response = await _http.GetAsync(url, cancellationToken);
        return response.StatusCode == System.Net.HttpStatusCode.OK;
    }

    public Task PushBranchAsync(BranchName branch, CancellationToken cancellationToken) =>
        RunGitWithCredentialsAsync(
            _writeToken.Value,
            ["push", $"https://github.com/{_repo.Value}.git", $"refs/heads/{branch.Value}:refs/heads/{branch.Value}"],
            cancellationToken);

    public async Task<string> CreatePullRequestAsync(
        BranchName branch,
        string title,
        string body,
        string baseBranch,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_repo.Value}/pulls";
        var payload = JsonSerializer.Serialize(
            new CreatePrRequestDto(Title: title, Head: branch.Value, Base: baseBranch, Body: body),
            GitHubJsonContext.Default.CreatePrRequestDto);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _writeToken.Value);

        var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub PR creation failed ({(int)response.StatusCode}): {json}");

        var pr = JsonSerializer.Deserialize(json, GitHubJsonContext.Default.CreatePrResponseDto)
            ?? throw new InvalidOperationException("GitHub returned null PR response");

        return pr.HtmlUrl;
    }

    private async Task RunGitWithCredentialsAsync(string token, string[] args, CancellationToken cancellationToken)
    {
        var credFile = Path.Combine(Path.GetTempPath(), $".rix-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(credFile, $"https://x-access-token:{token}@github.com\n");
            await RunGitAsync(["-c", $"credential.helper=store --file={credFile}", ..args], cancellationToken);
        }
        finally
        {
            if (File.Exists(credFile)) File.Delete(credFile);
        }
    }

    private async Task RunGitAsync(string[] args, CancellationToken cancellationToken)
    {
        var result = await _gitRunner(args, cancellationToken);
        if (!result.Succeeded)
        {
            var verb = Array.Find(args, a => !a.StartsWith('-')) ?? args[0];
            throw new InvalidOperationException($"git {verb} failed with exit code {result.ExitCode}");
        }
    }

    private static Task<ProcessResult> DefaultGitRunner(string[] args, CancellationToken cancellationToken) =>
        ProcessWrapper.RunAsync(
            "git", args,
            workingDirectory: Path.GetTempPath(),
            environment: ProcessWrapper.BuildSanitizedEnvironment(),
            cancellationToken: cancellationToken);

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
