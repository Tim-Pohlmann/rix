using System.Net;
using System.Text.Json;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class GitHubRepositoryHostTests
{
    private static GitHubRepositoryHost BuildHost(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string repo = "owner/repo",
        string readToken = "read-tok",
        string writeToken = "write-tok") =>
        GitHubRepositoryHost.WithHandler(new RepoIdentifier(repo), new ReadToken(readToken), new WriteToken(writeToken), new DelegatingHandlerStub(handler));

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_ReturnsTrue_When200()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var exists = await host.BranchExistsOnRemoteAsync("rix/some-branch", CancellationToken.None);
        Assert.IsTrue(exists);
    }

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_ReturnsFalse_When404()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var exists = await host.BranchExistsOnRemoteAsync("rix/missing", CancellationToken.None);
        Assert.IsFalse(exists);
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_ReturnsUrl_OnSuccess()
    {
        var host = BuildHost(_ =>
        {
            var body = JsonSerializer.Serialize(new { html_url = "https://github.com/owner/repo/pull/42" });
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        });

        var url = await host.CreatePullRequestAsync("rix/fix", "Fix bug", "Body text", CancellationToken.None);
        Assert.AreEqual("https://github.com/owner/repo/pull/42", url);
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_UsesWriteToken()
    {
        string? capturedAuth = null;
        var host = BuildHost(req =>
        {
            capturedAuth = req.Headers.Authorization?.Parameter;
            var body = JsonSerializer.Serialize(new { html_url = "https://github.com/owner/repo/pull/1" });
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }, writeToken: "my-write-token");

        await host.CreatePullRequestAsync("rix/branch", "Title", "Body", CancellationToken.None);
        Assert.AreEqual("my-write-token", capturedAuth);
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_Throws_OnErrorResponse()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"message\":\"Validation Failed\"}", System.Text.Encoding.UTF8, "application/json"),
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.CreatePullRequestAsync("rix/branch", "Title", "Body", CancellationToken.None));
    }

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
