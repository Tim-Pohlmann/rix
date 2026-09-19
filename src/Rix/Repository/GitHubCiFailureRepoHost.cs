using System.Text.Json.Serialization;

namespace Rix.Repository;

/// <summary>Answers the two questions <c>rix ci-failure</c> asks about the repo a failing run
/// belongs to: whether a PR is open for its branch, and how much of that branch's tip rix wrote
/// itself. A separate class from <see cref="GitHubJobRepoHost"/> rather than a second interface on
/// it, because the two roles share only their transport — which they now share explicitly, by being
/// handed the same <see cref="GitHubApi"/>. Reading the run itself is
/// <see cref="GitHubActionsCiHost"/>'s job.</summary>
internal sealed class GitHubCiFailureRepoHost : ICiFailureRepoHost
{
    private readonly GitHubApi _api;

    internal GitHubCiFailureRepoHost(GitHubApi api) => _api = api;

    internal GitHubCiFailureRepoHost(RepoIdentifier repo, GitReadToken token, HttpMessageHandler? handler = null)
        : this(new GitHubApi(repo, token, handler)) { }

    /// <summary>Finds the number of the open PR whose head is <paramref name="branch"/>, or
    /// <c>null</c> if there isn't one. Scoped to <see cref="RepoIdentifier.Owner"/>, so this only
    /// finds same-repo branches, never a fork's.</summary>
    public async Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken)
    {
        var head = Uri.EscapeDataString($"{_api.Repo.Owner}:{branch.Value}");
        var pulls = await _api.GetJsonAsync($"pulls?state=open&head={head}", GitHubCiFailureApiJsonContext.Default.ListPullRequestApiResponse, $"look up open PR for branch {branch.Value}", cancellationToken);
        return pulls.FirstOrDefault()?.Number;
    }

    /// <summary>Counts the run of rix's own commits at <paramref name="branch"/>'s tip, which is how
    /// <c>rix ci-failure</c> tells "CI failed" from "CI failed on rix's last attempt to fix it".
    /// Authorship is read from <c>commit.author</c>, git's own metadata written by
    /// <see cref="GitHubJobRepoHost.ConfigureGitAsync"/>, rather than the sibling top-level
    /// <c>author</c> — that one is the linked GitHub account, which is <c>null</c> for rix precisely
    /// because <see cref="GitIdentity.Email"/> belongs to no account. One page of at most
    /// <paramref name="max"/> commits answers it: a streak that long already trips the cap, so a
    /// second page could not change the outcome.</summary>
    public async Task<int> CountLeadingRixCommitsAsync(BranchName branch, MaxRixCommits max, CancellationToken cancellationToken)
    {
        var commits = await _api.GetJsonAsync
        (
            $"commits?sha={Uri.EscapeDataString(branch.Value)}&per_page={max.Value}",
            GitHubCiFailureApiJsonContext.Default.ListCommitApiResponse,
            $"list commits on branch {branch.Value}",
            cancellationToken
        );
        return commits.TakeWhile(commit => commit.Commit?.Author?.Email == GitIdentity.Email).Count();
    }
}

/// <summary>The JSON body of one entry of a GitHub "list commits" REST response, kept down to the
/// nesting the loop guard actually reads: <c>commit.author.email</c>.</summary>
internal sealed record CommitApiResponse
(
    [property: JsonPropertyName("commit")] CommitDetailApiResponse? Commit
);

internal sealed record CommitDetailApiResponse
(
    [property: JsonPropertyName("author")] CommitAuthorApiResponse? Author
);

internal sealed record CommitAuthorApiResponse
(
    [property: JsonPropertyName("email")] string? Email
);

/// <summary>The one field <c>rix ci-failure</c> reads from a "list pull requests" REST response.</summary>
internal sealed record PullRequestApiResponse
(
    [property: JsonPropertyName("number")] int Number
);

/// <summary>Separate from <see cref="GitHubApiJsonContext"/> (defined in <c>GitHubSubmitRepoHost.cs</c>):
/// splitting one <see cref="JsonSerializerContext"/>'s <c>[JsonSerializable]</c> attributes across
/// multiple files trips a source-generator bug (duplicate-hint-name failure), so these DTOs get
/// their own context instead.</summary>
[JsonSerializable(typeof(List<PullRequestApiResponse>))]
[JsonSerializable(typeof(List<CommitApiResponse>))]
internal partial class GitHubCiFailureApiJsonContext : JsonSerializerContext { }
