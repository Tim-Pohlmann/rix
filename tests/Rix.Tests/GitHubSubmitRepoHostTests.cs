using Rix.Repository;
using System.Net;

namespace Rix.Tests;

[TestClass]
public class GitHubSubmitRepoHostTests
{
    [TestMethod]
    public async Task CreatePullRequestAsync_PostsToPullsEndpoint_AndReturnsHtmlUrl()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var host = BuildHost(req =>
        {
            captured = req;
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"number":12,"html_url":"https://github.com/owner/repo/pull/12"}""",
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        });

        var url = await host.CreatePullRequestAsync(SamplePr("My title", "My body"), CancellationToken.None);

        Assert.AreEqual("https://github.com/owner/repo/pull/12", url);
        Assert.IsNotNull(captured);
        Assert.AreEqual(HttpMethod.Post, captured.Method);
        StringAssert.EndsWith(captured.RequestUri!.AbsoluteUri, "/repos/owner/repo/pulls");
        Assert.IsNotNull(body);
        StringAssert.Contains(body, "\"head\":\"rix/fix\"");
        StringAssert.Contains(body, "\"base\":\"main\"");
        StringAssert.Contains(body, "\"title\":\"My title\"");
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_Throws_OnErrorResponse()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));

        await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => host.CreatePullRequestAsync(SamplePr("t", "b"), CancellationToken.None));
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_Throws_WhenResponseIsNotParseableJson()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("not json"),
        });

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => host.CreatePullRequestAsync(SamplePr("t", "b"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "create pull request for rix/fix");
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_WrapsTransportFailure()
    {
        var host = BuildHost(_ => throw new HttpRequestException("connection refused"));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => host.CreatePullRequestAsync(SamplePr("t", "b"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "create pull request for rix/fix");
    }

    private static PendingPr SamplePr(string title, string body)
    => new
    (
        new RixBranchName("rix/fix"), new BranchName("main"),
        new PrTitle(title), new PrBody(body), "rix_2Ffix.bundle"
    );

    private static GitHubSubmitRepoHost BuildHost(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string repo = "owner/repo",
        string writeToken = "write-tok")
    => new
    (
        new RepoIdentifier(repo),
        new GitToken(writeToken),
        new DelegatingHandlerStub(handler)
    );
}
