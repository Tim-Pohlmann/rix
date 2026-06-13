using System.Net;
using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class GitHubRepositoryHostTests
{
    private static readonly Func<GitCommand, CancellationToken, Task<ProcessResult>> SuccessGitRunner =
        (_, _) => Task.FromResult<ProcessResult>(new ProcessSuccess());

    private static readonly string[] ExpectedBundleArgs =
        ["bundle", "create", "/tmp/out/fix.bundle", "main..rix/fix"];

    private static GitHubRepositoryHost BuildHost(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string repo = "owner/repo",
        string readToken = "read-tok",
        Func<GitCommand, CancellationToken, Task<ProcessResult>>? gitRunner = null) =>
        new(
            new RepoIdentifier(repo),
            new ReadToken(readToken),
            new DelegatingHandlerStub(handler),
            gitRunner ?? SuccessGitRunner);

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
            gitRunner: (cmd, _) => { capturedArgs = cmd.Args; return Task.FromResult<ProcessResult>(new ProcessSuccess()); });

        await host.CloneAsync("/tmp/target", CancellationToken.None);

        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual("clone", capturedArgs[0]);
        Assert.IsTrue(capturedArgs[1].Contains("my-read-token"), "Token must be embedded in clone URL");
        Assert.AreEqual("/tmp/target", capturedArgs[2]);
    }

    [TestMethod]
    public async Task CreateBundleAsync_CallsGitBundle_InRepoDirectory_WithCorrectArgs()
    {
        string[]? capturedArgs = null;
        string? capturedWorkingDir = null;
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (cmd, _) =>
            {
                capturedArgs = cmd.Args;
                capturedWorkingDir = cmd.WorkingDirectory;
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await host.CreateBundleAsync("/tmp/clone", "/tmp/out/fix.bundle",
            new BranchName("main"), new BranchName("rix/fix"), CancellationToken.None);

        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual("/tmp/clone", capturedWorkingDir);
        CollectionAssert.AreEqual(ExpectedBundleArgs, capturedArgs);
    }

    [TestMethod]
    public async Task CreateBundleAsync_Throws_WhenGitFails()
    {
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.CreateBundleAsync("/tmp/clone", "/tmp/out/fix.bundle",
                new BranchName("main"), new BranchName("rix/fix"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "bundle");
    }

    [TestMethod]
    public async Task CloneAsync_Throws_WhenGitFails()
    {
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.CloneAsync("/tmp/target", CancellationToken.None));
        StringAssert.Contains(ex.Message, "clone");
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
