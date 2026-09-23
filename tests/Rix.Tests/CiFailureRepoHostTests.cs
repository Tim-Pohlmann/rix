using Rix.Repository;
using System.Net;
using System.Text;

namespace Rix.Tests;

/// <summary>Covers <see cref="GitHubCiFailureRepoHost"/>: looking up an open PR for a branch, and
/// counting rix's own commits at that branch's tip. Only the REST side is stubbed - this host runs
/// no git commands. Reading the run itself belongs to
/// <see cref="GitHubActionsCiHostTests"/>.</summary>
[TestClass]
public class CiFailureRepoHostTests
{
    private static GitHubCiFailureRepoHost BuildHost(Func<HttpRequestMessage, HttpResponseMessage> handler, string repo = "owner/repo")
    => new(new RepoIdentifier(repo), new GitReadToken("read-tok"), new DelegatingHandlerStub(handler));

    private static HttpResponseMessage Json(string body)
    => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task FindOpenPullRequestNumberAsync_ReturnsNumber_WhenPrExists()
    {
        var host = BuildHost(_ => Json("""[{"number":42}]"""));

        var number = await host.FindOpenPullRequestNumberAsync(new BranchName("rix/fix"), CancellationToken.None);

        Assert.AreEqual(42, number);
    }

    [TestMethod]
    public async Task FindOpenPullRequestNumberAsync_ReturnsNull_WhenNoOpenPr()
    {
        var host = BuildHost(_ => Json("[]"));

        var number = await host.FindOpenPullRequestNumberAsync(new BranchName("rix/fix"), CancellationToken.None);

        Assert.IsNull(number);
    }

    [TestMethod]
    public async Task FindOpenPullRequestNumberAsync_ScopesHeadFilterToRepoOwner()
    {
        Uri? capturedUri = null;
        var host = BuildHost(request => { capturedUri = request.RequestUri; return Json("[]"); });

        await host.FindOpenPullRequestNumberAsync(new BranchName("rix/fix"), CancellationToken.None);

        Assert.IsNotNull(capturedUri);
        StringAssert.Contains(Uri.UnescapeDataString(capturedUri!.Query), "head=owner:rix/fix");
    }

    /// <summary>A commit rix made: the git author metadata carries <see cref="GitIdentity.Email"/>,
    /// while the top-level <c>author</c> - the linked GitHub account - is null, since that address
    /// belongs to no account.</summary>
    private const string RixCommit = """{"author":null,"commit":{"author":{"email":"rix@noreply.invalid"}}}""";

    private const string HumanCommit = """{"author":{"login":"someone"},"commit":{"author":{"email":"someone@example.com"}}}""";

    private static readonly MaxRixCommits Cap = new(5);

    [TestMethod]
    public async Task CountLeadingRixCommitsAsync_CountsTheStreakAtTheTip()
    {
        var host = BuildHost(_ => Json($"[{RixCommit},{RixCommit},{HumanCommit},{RixCommit}]"));

        var count = await host.CountLeadingRixCommitsAsync(new BranchName("rix/fix"), Cap, CancellationToken.None);

        // Stops at the human commit: the one after it is rix's again but no longer part of the run.
        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public async Task CountLeadingRixCommitsAsync_ReturnsZero_WhenSomeoneElseCommittedLast()
    {
        var host = BuildHost(_ => Json($"[{HumanCommit},{RixCommit}]"));

        Assert.AreEqual(0, await host.CountLeadingRixCommitsAsync(new BranchName("rix/fix"), Cap, CancellationToken.None));
    }

    [TestMethod]
    public async Task CountLeadingRixCommitsAsync_ReturnsZero_ForAnEmptyOrAuthorlessHistory()
    {
        Assert.AreEqual(0, await BuildHost(_ => Json("[]")).CountLeadingRixCommitsAsync(new BranchName("rix/fix"), Cap, CancellationToken.None));
        Assert.AreEqual(0, await BuildHost(_ => Json("""[{"commit":{}}]""")).CountLeadingRixCommitsAsync(new BranchName("rix/fix"), Cap, CancellationToken.None));
    }

    [TestMethod]
    public async Task CountLeadingRixCommitsAsync_AsksForTheBranchAndNoMoreCommitsThanTheCap()
    {
        Uri? capturedUri = null;
        var host = BuildHost(request => { capturedUri = request.RequestUri; return Json("[]"); });

        await host.CountLeadingRixCommitsAsync(new BranchName("rix/fix"), new MaxRixCommits(3), CancellationToken.None);

        Assert.IsNotNull(capturedUri);
        var query = Uri.UnescapeDataString(capturedUri!.Query);
        StringAssert.Contains(query, "sha=rix/fix");
        StringAssert.Contains(query, "per_page=3");
    }

    [TestMethod]
    public async Task CountLeadingRixCommitsAsync_Throws_OnErrorStatus()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>
        (
            () => host.CountLeadingRixCommitsAsync(new BranchName("rix/fix"), Cap, CancellationToken.None)
        );
        StringAssert.Contains(ex.Message, "list commits on branch rix/fix");
    }
}
