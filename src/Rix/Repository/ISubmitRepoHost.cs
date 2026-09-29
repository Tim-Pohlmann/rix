namespace Rix.Repository;

/// <summary>The host operation <c>rix submit</c> needs beyond git itself: opening a pull request,
/// which is a host concept rather than a git one. Requires a write credential.</summary>
internal interface ISubmitRepoHost
{
    Task<string> CreatePullRequestAsync(PendingPr pullRequest, CancellationToken cancellationToken);
}
