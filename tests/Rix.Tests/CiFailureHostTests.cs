using Rix.Process;
using Rix.Repository;
using System.Net;
using System.Text;

namespace Rix.Tests;

/// <summary>Covers the <see cref="IGitHubCiFailureHost"/> methods <see cref="GitHubReadHost"/>
/// implements: fetching a run's facts, concatenating its failed jobs' logs, and looking up an
/// open PR for its branch.</summary>
[TestClass]
public class CiFailureHostTests
{
    private static readonly RunProcessAsync SuccessGitRunner =
        (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessSuccess());

    /// <summary>Generous enough that the tests not about truncation are unaffected by it.</summary>
    private const int TailChars = 10_000;

    private static GitHubReadHost BuildHost(Func<HttpRequestMessage, HttpResponseMessage> handler, string repo = "owner/repo")
    => new(TestConfig.Repo(repo), new GitReadToken("read-tok"), SuccessGitRunner, new DelegatingHandlerStub(handler));

    private static HttpResponseMessage Json(string body)
    => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task GetRunAsync_ReturnsWorkflowRun_ForValidResponse()
    {
        var host = BuildHost(_ => Json(
            """{"conclusion":"failure","display_title":"Fix thing","html_url":"https://github.com/owner/repo/actions/runs/1","head_branch":"rix/fix"}"""));

        var run = await host.GetRunAsync(new RunId(1), CancellationToken.None);

        Assert.AreEqual("failure", run.Conclusion);
        Assert.AreEqual("Fix thing", run.DisplayTitle);
        Assert.AreEqual("https://github.com/owner/repo/actions/runs/1", run.HtmlUrl);
        Assert.AreEqual("rix/fix", run.HeadBranch);
    }

    [TestMethod]
    public async Task GetRunAsync_Throws_WhenRequiredFieldMissing()
    {
        var host = BuildHost(_ => Json("""{"conclusion":"failure"}"""));

        await Assert.ThrowsExactlyAsync<RepositoryHostException>(() => host.GetRunAsync(new RunId(1), CancellationToken.None));
    }

    [TestMethod]
    public async Task GetRunAsync_ReturnsNullConclusion_ForInProgressRun()
    {
        var host = BuildHost(_ => Json(
            """{"conclusion":null,"display_title":"Fix thing","html_url":"https://github.com/owner/repo/actions/runs/1","head_branch":"rix/fix"}"""));

        var run = await host.GetRunAsync(new RunId(1), CancellationToken.None);

        Assert.IsNull(run.Conclusion);
    }

    [TestMethod]
    public async Task GetRunAsync_Throws_OnErrorStatus()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsExactlyAsync<RepositoryHostException>(() => host.GetRunAsync(new RunId(1), CancellationToken.None));
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_ConcatenatesOnlyFailedJobLogs()
    {
        var host = BuildHost(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs"))
                return Json("""{"jobs":[{"id":1,"conclusion":"failure"},{"id":2,"conclusion":"success"},{"id":3,"conclusion":"failure"}]}""");
            if (request.RequestUri.AbsolutePath.EndsWith("/jobs/1/logs"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("log one") };
            if (request.RequestUri.AbsolutePath.EndsWith("/jobs/3/logs"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("log three") };
            throw new InvalidOperationException($"unexpected request: {request.RequestUri}");
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None);

        Assert.AreEqual("log one\nlog three", logs);
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_ReturnsEmptyString_WhenNoJobsFailed()
    {
        var host = BuildHost(_ => Json("""{"jobs":[{"id":1,"conclusion":"success"}]}"""));

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None);

        Assert.AreEqual("", logs);
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_KeepsOnlyTheTailOfEachJobLog()
    {
        var host = BuildHost(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs"))
                return Json("""{"jobs":[{"id":1,"conclusion":"failure"},{"id":2,"conclusion":"failure"}]}""");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 500) + "END") };
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), tailCharsPerJob: 10, CancellationToken.None);

        // Each job contributes its last 10 chars only, so nothing scales with the full log size.
        Assert.AreEqual("xxxxxxxEND\nxxxxxxxEND", logs);
    }

    /// <summary>The jobs endpoint is paginated, and a matrix build can exceed one page. The job that
    /// failed is no more likely to be on the first page than the last, so stopping there would
    /// produce an empty excerpt for exactly the runs this exists to explain.</summary>
    [TestMethod]
    public async Task GetFailedJobLogsAsync_FindsFailedJob_OnAPageAfterTheFirst()
    {
        var firstPage = string.Join(",", Enumerable.Range(1, 100).Select(id => $$"""{"id":{{id}},"conclusion":"success"}"""));
        var host = BuildHost(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs/101/logs"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("log from the last page") };
            if (request.RequestUri.Query.Contains("&page=1"))
                return Json($$"""{"jobs":[{{firstPage}}]}""");
            if (request.RequestUri.Query.Contains("&page=2"))
                return Json("""{"jobs":[{"id":101,"conclusion":"failure"}]}""");
            throw new InvalidOperationException($"unexpected request: {request.RequestUri}");
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None);

        Assert.AreEqual("log from the last page", logs);
    }

    /// <summary>A log far larger than one stream read, so the retained tail has to be carried across
    /// several reads rather than sliced out of a single fully-materialized string.</summary>
    [TestMethod]
    public async Task GetFailedJobLogsAsync_KeepsTail_WhenItSpansStreamReads()
    {
        var hugeLog = new string('x', 50_000) + "END-OF-LOG";
        var host = BuildHost(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/logs"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(hugeLog) };
            return Json("""{"jobs":[{"id":1,"conclusion":"failure"}]}""");
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), tailCharsPerJob: 20, CancellationToken.None);

        Assert.AreEqual(new string('x', 10) + "END-OF-LOG", logs);
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_Throws_WhenJobsFieldMissing()
    {
        var host = BuildHost(_ => Json("{}"));

        await Assert.ThrowsExactlyAsync<RepositoryHostException>(() => host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None));
    }

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
}
