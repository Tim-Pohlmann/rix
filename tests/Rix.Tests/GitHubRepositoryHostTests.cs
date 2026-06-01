using System.Net;
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
        Func<string[], CancellationToken, Task<ProcessResult>>? gitRunner = null) =>
        new(
            new RepoIdentifier(repo),
            new ReadToken(readToken),
            new DelegatingHandlerStub(handler),
            gitRunner);

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
            gitRunner: (args, _) => { capturedArgs = args; return Task.FromResult(new ProcessResult(0, false)); });

        await host.CloneAsync("/tmp/target", CancellationToken.None);

        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual("-c", capturedArgs[0]);
        Assert.AreEqual("clone", capturedArgs[2]);
        Assert.IsFalse(capturedArgs.Any(a => a.Contains("my-read-token")), "Token must not appear in git args");
        Assert.AreEqual("/tmp/target", capturedArgs[4]);
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
    public async Task CloneAsync_ErrorMessage_ContainsVerb_NotCredentialHelper()
    {
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _) => Task.FromResult(new ProcessResult(128, false)));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.CloneAsync("/tmp/target", CancellationToken.None));
        StringAssert.Contains(ex.Message, "clone");
        Assert.IsFalse(ex.Message.Contains("credential"), "Error message must not leak credential helper path");
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
