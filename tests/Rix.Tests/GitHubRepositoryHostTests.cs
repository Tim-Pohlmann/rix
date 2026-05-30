using System.Net;
using System.Text.Json;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class GitHubRepositoryHostTests
{
    private static readonly Func<string[], CancellationToken, Task<ProcessResult>> SuccessGitRunner =
        (_, _) => Task.FromResult(new ProcessResult(0, false));

    private static GitHubRepositoryHost BuildHost(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string repo = "owner/repo",
        string readToken = "read-tok",
        string writeToken = "write-tok",
        Func<string[], CancellationToken, Task<ProcessResult>>? gitRunner = null) =>
        GitHubRepositoryHost.WithHandler(
            RepoIdentifier.Parse(repo),
            new ReadToken(readToken),
            new WriteToken(writeToken),
            new DelegatingHandlerStub(handler),
            gitRunner);

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_ReturnsTrue_When200()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.IsTrue(await host.BranchExistsOnRemoteAsync("rix/some-branch", CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_ReturnsFalse_When404()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.IsFalse(await host.BranchExistsOnRemoteAsync("rix/missing", CancellationToken.None));
    }

    [TestMethod]
    public async Task CloneAsync_CallsGitClone_WithCorrectArgs()
    {
        string[]? capturedArgs = null;
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            readToken: "my-read-token",
            gitRunner: (args, _) => { capturedArgs = args; return Task.FromResult(new ProcessResult(0, false)); });

        await host.CloneAsync("/tmp/target", CancellationToken.None);

        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual("clone", capturedArgs[0]);
        StringAssert.Contains(capturedArgs[1], "my-read-token");
        Assert.AreEqual("/tmp/target", capturedArgs[2]);
    }

    [TestMethod]
    public async Task PushBranchAsync_CallsGitPush_WithCorrectArgs()
    {
        string[]? capturedArgs = null;
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            writeToken: "my-write-token",
            gitRunner: (args, _) => { capturedArgs = args; return Task.FromResult(new ProcessResult(0, false)); });

        await host.PushBranchAsync("rix/fix", CancellationToken.None);

        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual("push", capturedArgs[0]);
        StringAssert.Contains(capturedArgs[1], "my-write-token");
        StringAssert.Contains(capturedArgs[2], "rix/fix");
    }

    [TestMethod]
    public async Task CloneAsync_Throws_WhenGitFails()
    {
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _) => Task.FromResult(new ProcessResult(128, false)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.CloneAsync("/tmp/target", CancellationToken.None));
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
        }, gitRunner: SuccessGitRunner);

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
        }, writeToken: "my-write-token", gitRunner: SuccessGitRunner);

        await host.CreatePullRequestAsync("rix/branch", "Title", "Body", CancellationToken.None);
        Assert.AreEqual("my-write-token", capturedAuth);
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_Throws_OnErrorResponse()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"message\":\"Validation Failed\"}", System.Text.Encoding.UTF8, "application/json"),
        }, gitRunner: SuccessGitRunner);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.CreatePullRequestAsync("rix/branch", "Title", "Body", CancellationToken.None));
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_Throws_OnNullResponse()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json"),
        }, gitRunner: SuccessGitRunner);

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
