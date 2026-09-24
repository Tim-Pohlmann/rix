using System.Text.Json.Serialization;

namespace Rix.Repository;

/// <summary>The GitHub host behind <c>rix submit</c>: opens pull requests over the REST API.
/// Requires a write-capable <see cref="GitToken"/>; the push itself goes through
/// <see cref="GitCli"/>.</summary>
internal sealed class GitHubSubmitRepoHost : ISubmitRepoHost
{
    private readonly GitHubApi _api;

    internal GitHubSubmitRepoHost(RepoIdentifier repo, GitToken token, HttpMessageHandler? handler = null)
    => _api = new GitHubApi(repo, token, handler);

    /// <summary>Creates the pull request and returns its <c>html_url</c>, so the caller can report
    /// (and link) the opened PR rather than only its branch name.</summary>
    public async Task<string> CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken)
    {
        var request = new CreatePullRequestRequest
        (
            Title: pullRequest.Title.Value,
            Head: pullRequest.Branch.Value,
            Base: pullRequest.BaseBranch.Value,
            Body: pullRequest.Body.Value
        );
        var created = await _api.PostJsonAsync
        (
            "pulls",
            request,
            GitHubApiJsonContext.Default.CreatePullRequestRequest,
            GitHubApiJsonContext.Default.CreatePullRequestResponse,
            $"create pull request for {pullRequest.Branch.Value}",
            cancellationToken
        );
        if (created.HtmlUrl is null)
            throw new RepoHostException("create PR response did not include html_url");
        return created.HtmlUrl;
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

/// <summary>The field <c>rix submit</c> reads back from a successful "create a pull request"
/// response.</summary>
internal sealed record CreatePullRequestResponse
(
    [property: JsonPropertyName("html_url")] string? HtmlUrl
);

[JsonSerializable(typeof(CreatePullRequestRequest))]
[JsonSerializable(typeof(CreatePullRequestResponse))]
internal partial class GitHubApiJsonContext : JsonSerializerContext { }
