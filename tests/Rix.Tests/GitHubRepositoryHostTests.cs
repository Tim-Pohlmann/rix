using System.Net;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class GitHubRepositoryHostTests
{
    private static readonly RunProcessAsync SuccessGitRunner =
        (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessSuccess());

    private static GitHubRepositoryHost BuildHost(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string repo = "owner/repo",
        string readToken = "read-tok",
        RunProcessAsync? gitRunner = null) =>
        new(
            TestConfig.Repo(repo),
            new ReadToken(readToken),
            gitRunner ?? SuccessGitRunner,
            new DelegatingHandlerStub(handler));

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_ReturnsTrue_When200()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.OK));
        Assert.IsTrue(await host.BranchExistsOnRemoteAsync(new BranchName("rix/some-branch"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_ReturnsFalse_When404()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.IsFalse(await host.BranchExistsOnRemoteAsync(new BranchName("rix/missing"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_Throws_ForNon404Error()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => host.BranchExistsOnRemoteAsync(new BranchName("rix/branch"), CancellationToken.None));
    }

    [TestMethod]
    public async Task CloneAsync_CallsGitClone_WithCorrectArgs()
    {
        string[]? capturedArgs = null;
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            readToken: "my-read-token",
            gitRunner: (_, args, _, _, _, _) => { capturedArgs = args.ToArray(); return Task.FromResult<ProcessResult>(new ProcessSuccess()); });

        await host.CloneAsync("/tmp/target", CancellationToken.None);

        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual("clone", capturedArgs[0]);
        Assert.IsTrue(capturedArgs[1].Contains("my-read-token"), "Token must be embedded in clone URL");
        Assert.AreEqual("/tmp/target", capturedArgs[2]);
    }

    [TestMethod]
    public async Task CloneAsync_Throws_WhenGitFails()
    {
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.CloneAsync("/tmp/target", CancellationToken.None));
        StringAssert.Contains(ex.Message, "clone");
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_PostsToPullsEndpoint_WithPrFields()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var host = BuildWriteHost(req =>
        {
            captured = req;
            body = req.Content!.ReadAsStringAsync().Result;
            return new HttpResponseMessage(HttpStatusCode.Created);
        });

        await host.CreatePullRequestAsync(SamplePr("My title", "My body"), CancellationToken.None);

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
        var host = BuildWriteHost(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => host.CreatePullRequestAsync(SamplePr("t", "b"), CancellationToken.None));
    }

    private static PendingPr SamplePr(string title, string body) =>
        new(new RixBranchName("rix/fix"), new BranchName("main"),
            new PrTitle(title), new PrBody(body), "rix_2Ffix.bundle");

    private static GitHubRepositoryHost BuildWriteHost(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string repo = "owner/repo",
        string writeToken = "write-tok") =>
        new(
            TestConfig.Repo(repo),
            new WriteToken(writeToken),
            SuccessGitRunner,
            new DelegatingHandlerStub(handler));

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
