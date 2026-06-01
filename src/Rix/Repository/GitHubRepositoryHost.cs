using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Rix.Process;

namespace Rix.Repository;

internal sealed class GitHubRepositoryHost : IRepositoryHost
{
    private readonly RepoIdentifier _repo;
    private readonly ReadToken _readToken;
    private readonly HttpClient _http;
    private readonly Func<string[], CancellationToken, Task<ProcessResult>> _gitRunner;

    internal GitHubRepositoryHost(RepoIdentifier repo, ReadToken readToken)
        : this(repo, readToken, handler: null, gitRunner: null) { }

    private GitHubRepositoryHost(
        RepoIdentifier repo,
        ReadToken readToken,
        HttpMessageHandler? handler,
        Func<string[], CancellationToken, Task<ProcessResult>>? gitRunner)
    {
        _repo = repo;
        _readToken = readToken;
        _http = BuildHttpClient(readToken, handler);
        _gitRunner = gitRunner ?? DefaultGitRunner;
    }

    internal static GitHubRepositoryHost WithHandler(
        RepoIdentifier repo,
        ReadToken readToken,
        HttpMessageHandler handler,
        Func<string[], CancellationToken, Task<ProcessResult>>? gitRunner = null) =>
        new(repo, readToken, handler, gitRunner);

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
            "clone",
            ["clone", $"https://github.com/{_repo.Value}.git", targetDirectory],
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

    private async Task RunGitWithCredentialsAsync(string verb, string[] args, CancellationToken cancellationToken)
    {
        var credFile = Path.Combine(Path.GetTempPath(), $".rix-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllTextAsync(credFile, $"https://x-access-token:{_readToken.Value}@github.com\n", cancellationToken);
            await RunGitAsync(verb, ["-c", $"credential.helper=store --file={credFile}", ..args], cancellationToken);
        }
        finally
        {
            if (File.Exists(credFile)) File.Delete(credFile);
        }
    }

    private async Task RunGitAsync(string verb, string[] args, CancellationToken cancellationToken)
    {
        var result = await _gitRunner(args, cancellationToken);
        if (!result.Succeeded)
            throw new InvalidOperationException($"git {verb} failed with exit code {result.ExitCode}");
    }

    [ExcludeFromCodeCoverage]
    private static Task<ProcessResult> DefaultGitRunner(string[] args, CancellationToken cancellationToken) =>
        ProcessWrapper.RunAsync(
            "git", args,
            workingDirectory: Path.GetTempPath(),
            environment: new Dictionary<string, string>
            {
                ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "",
                ["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "",
            },
            cancellationToken: cancellationToken);
}
