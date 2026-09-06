using Rix.Process;
using Rix.Repository;
using System.Net;
using System.Text;

namespace Rix.Tests;

/// <summary>Covers the <see cref="ICiFailureHost"/> methods <see cref="GitHubReadHost"/>
/// implements: fetching a run's facts, concatenating its failed jobs' logs, and looking up an
/// open PR for its branch.</summary>
[TestClass]
public class CiFailureHostTests
{
    private static readonly RunProcessAsync SuccessGitRunner =
        (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessSuccess());

    private static GitHubReadHost BuildHost(Func<HttpRequestMessage, HttpResponseMessage> handler, string repo = "owner/repo")
    => new(TestConfig.Repo(repo), new GitReadToken("read-tok"), SuccessGitRunner, new DelegatingHandlerStub(handler));

    private static HttpResponseMessage Json(string body)
    => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task GetRunAsync_ReturnsWorkflowRun_ForValidResponse()
    {
        var host = BuildHost(_ => Json(
            """{"conclusion":"failure","display_title":"Fix thing","html_url":"https://github.com/owner/repo/actions/runs/1","head_branch":"rix/fix"}"""));

        var run = await host.GetRunAsync(1, CancellationToken.None);

        Assert.AreEqual("failure", run.Conclusion);
        Assert.AreEqual("Fix thing", run.DisplayTitle);
        Assert.AreEqual("https://github.com/owner/repo/actions/runs/1", run.HtmlUrl);
        Assert.AreEqual("rix/fix", run.HeadBranch);
    }

    [TestMethod]
    public async Task GetRunAsync_Throws_WhenRequiredFieldMissing()
    {
        var host = BuildHost(_ => Json("""{"conclusion":"failure"}"""));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => host.GetRunAsync(1, CancellationToken.None));
    }

    [TestMethod]
    public async Task GetRunAsync_Throws_OnErrorStatus()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => host.GetRunAsync(1, CancellationToken.None));
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

        var logs = await host.GetFailedJobLogsAsync(1, CancellationToken.None);

        Assert.AreEqual("log one\nlog three", logs);
    }

    [TestMethod]
    public async Task GetFailedJobLogsAsync_ReturnsEmptyString_WhenNoJobsFailed()
    {
        var host = BuildHost(_ => Json("""{"jobs":[{"id":1,"conclusion":"success"}]}"""));

        var logs = await host.GetFailedJobLogsAsync(1, CancellationToken.None);

        Assert.AreEqual("", logs);
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

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        => Task.FromResult(handler(request));
    }
}
