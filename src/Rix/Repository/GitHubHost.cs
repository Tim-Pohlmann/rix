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

    public Task ConfigureGitAsync(string repoDirectory, CancellationToken cancellationToken)
    => _read.ConfigureGitAsync(repoDirectory, cancellationToken);

    public Task<IReadOnlyList<RemotePr>> ListOpenPullRequestsAsync(CancellationToken cancellationToken)
    => _read.ListOpenPullRequestsAsync(cancellationToken);

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

    /// <summary>Updates an already-submitted task's pull request — its title and/or body. Locates the
    /// open PR by the task's branch, then PATCHes it; returns the PR's <c>html_url</c>. Throws
    /// <see cref="InvalidOperationException"/> when the branch has no open PR (nothing was submitted
    /// for it, or its PR was already closed/merged).</summary>
    public async Task<string> UpdatePullRequestAsync(PendingTaskUpdate update, CancellationToken cancellationToken)
    {
        var pullRequest = await FindOpenPullRequestAsync(update.Branch, cancellationToken)
            ?? throw new InvalidOperationException($"no open pull request found for branch {update.Branch.Value}");
        return await PatchPullRequestAsync
        (
            pullRequest,
            new UpdatePullRequestRequest(Title: update.Title?.Value, Body: update.Body?.Value, State: null),
            cancellationToken
        );
    }

    /// <summary>Reverts an already-submitted task by closing its open pull request. Locates the open
    /// PR by the task's branch, then PATCHes its state to <c>"closed"</c>; returns the PR's
    /// <c>html_url</c>. Throws <see cref="InvalidOperationException"/> when the branch has no open PR.</summary>
    public async Task<string> ClosePullRequestAsync(PendingTaskRevert revert, CancellationToken cancellationToken)
    {
        var pullRequest = await FindOpenPullRequestAsync(revert.Branch, cancellationToken)
            ?? throw new InvalidOperationException($"no open pull request found for branch {revert.Branch.Value}");
        return await PatchPullRequestAsync
        (
            pullRequest,
            new UpdatePullRequestRequest(Title: null, Body: null, State: "closed"),
            cancellationToken
        );
    }

    /// <summary>Locates the branch's open pull request by listing open PRs and matching the head ref
    /// against the target repo (a fork PR's head repo is the fork, so it never matches). <c>null</c>
    /// when the branch has no open PR.</summary>
    private async Task<GitHubPullRequestItem?> FindOpenPullRequestAsync(RixBranchName branch, CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{_read.Repo.Value}/pulls?state=open&per_page=100";
        using var response = await _read.Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        try
        {
            var items = await response.Content.ReadFromJsonAsync
            (
                GitHubApiJsonContext.Default.GitHubPullRequestItemArray, cancellationToken
            );
            return items?.FirstOrDefault(i =>
                i.Head.Ref == branch.Value && i.Head.Repo?.FullName == _read.Repo.Value);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("could not parse list pull requests response", ex);
        }
    }

    /// <summary>PATCHes <paramref name="request"/> onto <paramref name="pullRequest"/> and returns
    /// the PR's <c>html_url</c>. Null fields are omitted from the wire body so GitHub keeps their
    /// current values — important for an update that only changes the title, and for a close that
    /// only sends <c>state</c>.</summary>
    private async Task<string> PatchPullRequestAsync
    (
        GitHubPullRequestItem pullRequest,
        UpdatePullRequestRequest request,
        CancellationToken cancellationToken
    )
    {
        var url = $"https://api.github.com/repos/{_read.Repo.Value}/pulls/{pullRequest.Number}";
        // Copy the source-generated context's options and add null-dropping, so a PATCH only
        // carries the fields the caller actually set (GitHub keeps omitted fields unchanged).
        var options = new JsonSerializerOptions(GitHubApiJsonContext.Default.Options)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        using var content = JsonContent.Create(request, mediaType: null, options);
        using var response = await _read.Http.PatchAsync(url, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        return pullRequest.HtmlUrl;
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

/// <summary>The body of a "update a pull request" REST call (also used to close one via
/// <c>state: "closed"</c>). Null fields are dropped before serialization, so GitHub keeps their
/// current values.</summary>
internal sealed record UpdatePullRequestRequest
(
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("state")] string? State
);

/// <summary>One entry of a "list pull requests" REST response — the fields the review/update/close
/// paths read. <c>head.repo.full_name</c> distinguishes the target repo's own branches from fork PRs.</summary>
internal sealed record GitHubPullRequestItem
(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("head")] GitHubPullRequestHead Head,
    [property: JsonPropertyName("base")] GitHubPullRequestBase Base
);

internal sealed record GitHubPullRequestHead
(
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("repo")] GitHubPullRequestRepo? Repo
);

internal sealed record GitHubPullRequestBase([property: JsonPropertyName("ref")] string Ref);

internal sealed record GitHubPullRequestRepo([property: JsonPropertyName("full_name")] string? FullName);

[JsonSerializable(typeof(CreatePullRequestRequest))]
[JsonSerializable(typeof(CreatePullRequestResponse))]
[JsonSerializable(typeof(UpdatePullRequestRequest))]
[JsonSerializable(typeof(GitHubPullRequestItem))]
[JsonSerializable(typeof(GitHubPullRequestItem[]))]
internal partial class GitHubApiJsonContext : JsonSerializerContext { }
