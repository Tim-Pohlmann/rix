namespace Rix.Repository;

/// <summary>The read-only repository operations needed to judge a CI failure, as opposed to reading
/// the failure itself: whether a pull request is open for the failing branch, and how much of that
/// branch's tip rix wrote. Both are questions about the repo's contents, so they live here rather
/// than on <see cref="ICiHost"/>, which covers the run. Kept separate from <see cref="IJobRepoHost"/>
/// so <c>rix job</c>'s stub host isn't forced to implement operations it never uses.</summary>
internal interface ICiFailureRepoHost
{
    Task<int?> FindOpenPullRequestNumberAsync(BranchName branch, CancellationToken cancellationToken);

    /// <summary>How many commits at <paramref name="branch"/>'s tip rix authored itself, counting
    /// back from the tip and stopping at the first commit it didn't — so anyone else pushing to the
    /// branch clears the streak, which is what makes a human stepping in enough to re-enable rix.
    /// Never reports more than <paramref name="max"/>: the caller only needs to know whether the
    /// streak reaches its cap, so counting past it would be work no answer depends on.</summary>
    Task<int> CountLeadingRixCommitsAsync(BranchName branch, MaxRixCommits max, CancellationToken cancellationToken);
}
