using Rix.Process;
using System.Text.Json.Serialization;

namespace Rix.Repository;

/// <summary>The full GitHub host behind <c>rix submit</c>: delegates every read operation to a
/// <see cref="GitHubJobRepoHost"/> and layers the write operations (push, open PR) on top of the same
/// <see cref="GitCli"/> and
/// <see cref="GitHubApi"/> that host was built from — so both paths share one connection pool and
/// one credential injection by construction. Requires a write-capable <see cref="GitToken"/>.</summary>
internal sealed class GitHubSubmitRepoHost : ISubmitRepoHost
{
    private readonly GitHubJobRepoHost _job;
    private readonly GitCli _git;
    private readonly GitHubApi _api;

    internal GitHubSubmitRepoHost
    (
        RepoIdentifier repo,
        GitToken token,
        RunProcessAsync runProcess,
        HttpMessageHandler? handler = null
    )
    {
        _git = new GitCli(token, runProcess);
        _api = new GitHubApi(repo, token, handler);
        _job = new GitHubJobRepoHost(_git, _api);
    }

    public Task CloneAsync(string targetDirectory, CancellationToken cancellationToken)
    => _job.CloneAsync(targetDirectory, cancellationToken);

    public Task<bool> BranchExistsOnRemoteAsync(BranchName branch, CancellationToken cancellationToken)
    => _job.BranchExistsOnRemoteAsync(branch, cancellationToken);

    public Task<bool> BranchExistsLocallyAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    => _job.BranchExistsLocallyAsync(repoDirectory, branch, cancellationToken);

    public Task ConfigureGitAsync(string repoDirectory, CancellationToken cancellationToken)
    => _job.ConfigureGitAsync(repoDirectory, cancellationToken);

    public Task CreateBundleAsync
    (
        string repoDirectory,
        string bundlePath,
        BranchName baseBranch,
        BranchName branch,
        CancellationToken cancellationToken
    )
    => _job.CreateBundleAsync(repoDirectory, bundlePath, baseBranch, branch, cancellationToken);

    public Task PushBranchAsync(string repoDirectory, BranchName branch, CancellationToken cancellationToken)
    => _git.RunAsync
    (
        // --end-of-options stops git from reading a branch name starting with "-" as an option —
        // see GitHubJobRepoHost.CreateBundleAsync for why it's this flag and not "--".
        ["push", "origin", "--end-of-options", branch.Value],
        workingDirectory: repoDirectory,
        authenticated: true,
        cancellationToken
    );

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
