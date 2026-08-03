using Rix.Process;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rix.Repository;

/// <summary>Full GitHub host: composes a <see cref="GitHubReadHost"/> for the read operations and
/// layers the write operations (push, open PR) on top of its shared transport. Requires a
/// write-capable <see cref="GitToken"/>.</summary>
internal sealed class GitHubHost : IRepositoryHost
{
    private readonly GitHubReadHost _read;

    internal GitHubHost
    (
        RepoIdentifier repo,
        GitToken token,
        RunProcessAsync runProcess,
        HttpMessageHandler? handler = null
    )
    => _read = new GitHubReadHost(repo, token, runProcess, handler);

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    => _read.CloneAsync(targetDirectory, cancellationToken);

    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    => _read.BranchExistsOnRemoteAsync(branch, cancellationToken);

    public Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    => _read.BranchExistsLocallyAsync(repoDirectory, branch, cancellationToken);

    public Task CreateBundleAsync
    (
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken
    )
    => _read.CreateBundleAsync(repoDirectory, bundlePath, baseBranch, branch, cancellationToken);

    public Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    => _read.RunGitAsync
    (
        ["push", "origin", branch.Value],
        workingDirectory: repoDirectory,
        authenticated: true,
        cancellationToken
    );

    /// <summary>Creates the pull request and returns its <c>html_url</c>, so the caller can report
    /// (and link) the opened PR rather than only its branch name.</summary>
    public async Task<string> CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_read.Repo.Value}/pulls";
        var request = new CreatePullRequestRequest
        (
            Title: pullRequest.Title.Value,
            Head: pullRequest.Branch.Value,
            Base: pullRequest.BaseBranch.Value,
            Body: pullRequest.Body.Value
        );
        using var content = JsonContent.Create(request, GitHubApiJsonContext.Default.CreatePullRequestRequest);
        using var response = await _read.Http.PostAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        try
        {
            var created = await response.Content.ReadFromJsonAsync
            (
                GitHubApiJsonContext.Default.CreatePullRequestResponse, cancellationToken
            );
            if (created is null || created.HtmlUrl is null)
                throw new HttpRequestException("create PR response did not include html_url");
            return created.HtmlUrl;
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("could not parse create PR response", ex);
        }
    }
}

/// <summary>The JSON body of a GitHub "create a pull request" REST call.</summary>
internal sealed record CreatePullRequestRequest
(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("head")] string Head,
    [property: JsonPropertyName("base")] string Base,
    [property: JsonPropertyName("body")] string Body
);

/// <summary>The fields <c>rix submit</c> reads back from a successful "create a pull request"
/// response.</summary>
internal sealed record CreatePullRequestResponse
(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("html_url")] string? HtmlUrl
);

[JsonSerializable(typeof(CreatePullRequestRequest))]
[JsonSerializable(typeof(CreatePullRequestResponse))]
internal partial class GitHubApiJsonContext : JsonSerializerContext { }
