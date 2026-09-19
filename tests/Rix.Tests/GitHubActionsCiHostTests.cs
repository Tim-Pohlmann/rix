using Rix.Repository;
using System.Net;
using System.Text;

namespace Rix.Tests;

/// <summary>Covers <see cref="GitHubActionsCiHost"/>: fetching a run's facts and concatenating its
/// failed jobs' logs. Only the REST side is stubbed - this host runs no git commands.</summary>
[TestClass]
public class GitHubActionsCiHostTests
{
    /// <summary>Generous enough that the tests not about truncation are unaffected by it.</summary>
    private const int TailChars = 10_000;

    private static GitHubActionsCiHost BuildHost(Func<HttpRequestMessage, HttpResponseMessage> handler, string repo = "owner/repo")
    => new(new RepoIdentifier(repo), new GitReadToken("read-tok"), new DelegatingHandlerStub(handler));

    private static HttpResponseMessage Json(string body)
    => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task GetRunAsync_ReturnsWorkflowRun_ForValidResponse()
    {
        var host = BuildHost(_ => Json(
            """{"conclusion":"failure","display_title":"Fix thing","html_url":"https://github.com/owner/repo/actions/runs/1","head_branch":"rix/fix","head_repository":{"full_name":"owner/repo"}}"""));

        var run = await host.GetRunAsync(new RunId(1), CancellationToken.None);

        Assert.AreEqual("failure", run.Conclusion);
        Assert.AreEqual("Fix thing", run.DisplayTitle);
        Assert.AreEqual("https://github.com/owner/repo/actions/runs/1", run.HtmlUrl);
        Assert.AreEqual("rix/fix", run.HeadBranch);
        Assert.AreEqual("owner/repo", run.HeadRepo);
    }

    /// <summary>head_repository is required like the rest: the caller decides whether to answer the
    /// run by comparing it, and there is no value it could safely stand in with.</summary>
    [TestMethod]
    public async Task GetRunAsync_Throws_WhenHeadRepositoryMissing()
    {
        var host = BuildHost(_ => Json(
            """{"conclusion":"failure","display_title":"Fix thing","html_url":"https://github.com/owner/repo/actions/runs/1","head_branch":"rix/fix"}"""));

        await Assert.ThrowsExactlyAsync<CiHostException>(() => host.GetRunAsync(new RunId(1), CancellationToken.None));
    }

    [TestMethod]
    public async Task GetRunAsync_Throws_WhenRequiredFieldMissing()
    {
        var host = BuildHost(_ => Json("""{"conclusion":"failure"}"""));

        await Assert.ThrowsExactlyAsync<CiHostException>(() => host.GetRunAsync(new RunId(1), CancellationToken.None));
    }

    [TestMethod]
    public async Task GetRunAsync_ReturnsNullConclusion_ForInProgressRun()
    {
        var host = BuildHost(_ => Json(
            """{"conclusion":null,"display_title":"Fix thing","html_url":"https://github.com/owner/repo/actions/runs/1","head_branch":"rix/fix","head_repository":{"full_name":"owner/repo"}}"""));

        var run = await host.GetRunAsync(new RunId(1), CancellationToken.None);

        Assert.IsNull(run.Conclusion);
    }

    [TestMethod]
    public async Task GetRunAsync_Throws_OnErrorStatus()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsExactlyAsync<CiHostException>(() => host.GetRunAsync(new RunId(1), CancellationToken.None));
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_HeadsEachFailedJobsLog_WithItsName()
    {
        var host = BuildHost(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs"))
                return Json("""{"jobs":[{"id":1,"name":"build","conclusion":"failure"},{"id":2,"name":"lint","conclusion":"success"},{"id":3,"name":"test","conclusion":"failure"}]}""");
            if (request.RequestUri.AbsolutePath.EndsWith("/jobs/1/logs"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("log one") };
            if (request.RequestUri.AbsolutePath.EndsWith("/jobs/3/logs"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("log three") };
            throw new InvalidOperationException($"unexpected request: {request.RequestUri}");
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None);

        // Without the headings the two logs read as one, and nothing says which job either came from.
        Assert.AreEqual("===== build =====\nlog one\n===== test =====\nlog three", logs);
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_FallsBackToTheJobId_WhenGitHubSendsNoName()
    {
        var host = BuildHost(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs"))
                return Json("""{"jobs":[{"id":7,"conclusion":"failure"}]}""");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("log seven") };
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None);

        Assert.AreEqual("===== job 7 =====\nlog seven", logs);
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_ReturnsEmptyString_WhenNoJobsFailed()
    {
        var host = BuildHost(_ => Json("""{"jobs":[{"id":1,"conclusion":"success"}]}"""));

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None);

        Assert.AreEqual("", logs);
    }

    /// <summary>The budget covers the excerpt, not each job: two failing jobs get half of it each,
    /// so what the caller asked to fit into a prompt is what it gets however many jobs failed.</summary>
    [TestMethod]
    public async Task GetFailedJobLogsAsync_SplitsTheBudgetAcrossFailedJobs()
    {
        var host = BuildHost(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs"))
                return Json("""{"jobs":[{"id":1,"name":"one","conclusion":"failure"},{"id":2,"name":"two","conclusion":"failure"}]}""");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 500) + "END") };
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), totalTailChars: 20, CancellationToken.None);

        Assert.AreEqual("===== one =====\nxxxxxxxEND\n===== two =====\nxxxxxxxEND", logs);
    }

    /// <summary>Past a handful of jobs each share of the budget is too short to show anything useful,
    /// and a matrix failing in many configurations is usually failing for one reason — so the rest are
    /// named as a count instead of being fetched and squeezed in.</summary>
    [TestMethod]
    public async Task GetFailedJobLogsAsync_CapsTheJobCount_AndSaysHowManyItLeftOut()
    {
        var jobs = string.Join(",", Enumerable.Range(1, 8).Select(id => $$"""{"id":{{id}},"name":"job-{{id}}","conclusion":"failure"}"""));
        var fetched = new List<string>();
        var host = BuildHost(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/jobs"))
                return Json($$"""{"jobs":[{{jobs}}]}""");
            fetched.Add(request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("log") };
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None);

        StringAssert.Contains(logs, "===== job-5 =====");
        Assert.IsFalse(logs.Contains("===== job-6 ====="), "jobs past the cap must not appear");
        StringAssert.EndsWith(logs, "===== 3 further failed job(s) omitted =====");
        Assert.AreEqual(5, fetched.Count, "logs past the cap must not even be fetched");
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
                return Json("""{"jobs":[{"id":101,"name":"last","conclusion":"failure"}]}""");
            throw new InvalidOperationException($"unexpected request: {request.RequestUri}");
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None);

        Assert.AreEqual("===== last =====\nlog from the last page", logs);
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
            return Json("""{"jobs":[{"id":1,"name":"build","conclusion":"failure"}]}""");
        });

        var logs = await host.GetFailedJobLogsAsync(new RunId(1), totalTailChars: 20, CancellationToken.None);

        Assert.AreEqual("===== build =====\n" + new string('x', 10) + "END-OF-LOG", logs);
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_Throws_WhenJobsFieldMissing()
    {
        var host = BuildHost(_ => Json("{}"));

        await Assert.ThrowsExactlyAsync<CiHostException>(() => host.GetFailedJobLogsAsync(new RunId(1), TailChars, CancellationToken.None));
    }
}
