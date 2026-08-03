using Rix.Process;
using Rix.Repository;
using System.Net;

namespace Rix.Tests;

[TestClass]
public class GitHubHostTests
{
    private static readonly RunProcessAsync SuccessGitRunner =
        (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessSuccess());

    private static readonly string[] ExpectedBundleArgs =
        ["bundle", "create", "/tmp/out/fix.bundle", "main..rix/fix"];

    private static readonly string[] ExpectedPushArgs = ["push", "origin", "rix/fix"];

    private static readonly string[] ExpectedBranchExistsLocallyArgs =
        ["rev-parse", "--verify", "--quiet", "refs/heads/rix/fix"];

    private static GitHubReadHost BuildHost(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string repo = "owner/repo",
        string readToken = "read-tok",
        RunProcessAsync? gitRunner = null)
    => new
    (
        TestConfig.Repo(repo),
        new GitReadToken(readToken),
        gitRunner ?? SuccessGitRunner,
        new DelegatingHandlerStub(handler)
    );

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
    public async Task BranchExistsLocallyAsync_ReturnsTrue_WhenGitRevParseSucceeds()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessSuccess()));

        Assert.IsTrue(await host.BranchExistsLocallyAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsLocallyAsync_ReturnsFalse_WhenGitRevParseFails()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")));

        Assert.IsFalse(await host.BranchExistsLocallyAsync("/tmp/clone", new BranchName("rix/missing"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsLocallyAsync_Throws_OnOperationalFailure_NotJustMissingRef()
    {
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.BranchExistsLocallyAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsLocallyAsync_RunsGitRevParse_InRepoDirectory_NoAuthEnv()
    {
        string[]? capturedArgs = null;
        string? capturedWorkingDir = null;
        IReadOnlyDictionary<string, string>? capturedEnv = null;
        var host = BuildHost(_ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, args, workingDir, env, _, _) =>
            {
                capturedArgs = args.ToArray();
                capturedWorkingDir = workingDir;
                capturedEnv = env;
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await host.BranchExistsLocallyAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None);

        CollectionAssert.AreEqual(ExpectedBranchExistsLocallyArgs, capturedArgs);
        Assert.AreEqual("/tmp/clone", capturedWorkingDir);
        Assert.IsNull(capturedEnv, "local-only check must not receive the credential env");
    }

    [TestMethod]
    public async Task CloneAsync_CallsGitClone_WithPlainUrl_NoTokenInArgs()
    {
        string[]? capturedArgs = null;
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            readToken: "my-read-token",
            gitRunner: (_, args, _, _, _, _) => { capturedArgs = args.ToArray(); return Task.FromResult<ProcessResult>(new ProcessSuccess()); });

        await host.CloneAsync("/tmp/target", CancellationToken.None);

        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual("clone", capturedArgs[0]);
        Assert.AreEqual("https://github.com/owner/repo.git", capturedArgs[1]);
        Assert.AreEqual("/tmp/target", capturedArgs[2]);
        Assert.IsFalse(
            capturedArgs.Any(a => a.Contains("my-read-token")), "Token must never appear in git arguments");
    }

    [TestMethod]
    public async Task CloneAsync_PassesTokenViaGitConfigEnv_NotInUrl()
    {
        IReadOnlyDictionary<string, string>? capturedEnv = null;
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            readToken: "my-read-token",
            gitRunner: (_, _, _, env, _, _) => { capturedEnv = env; return Task.FromResult<ProcessResult>(new ProcessSuccess()); });

        await host.CloneAsync("/tmp/target", CancellationToken.None);

        Assert.IsNotNull(capturedEnv);
        Assert.AreEqual("1", capturedEnv["GIT_CONFIG_COUNT"]);
        Assert.AreEqual("http.https://github.com/.extraheader", capturedEnv["GIT_CONFIG_KEY_0"]);
        var expected = "Authorization: Basic " +
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("x-access-token:my-read-token"));
        Assert.AreEqual(expected, capturedEnv["GIT_CONFIG_VALUE_0"]);
    }

    [TestMethod]
    public async Task CreateBundleAsync_PassesNoAuthEnv_BecauseBundleIsLocalOnly()
    {
        IReadOnlyDictionary<string, string>? capturedEnv = null;
        var captured = false;
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _, _, env, _, _) => { capturedEnv = env; captured = true; return Task.FromResult<ProcessResult>(new ProcessSuccess()); });

        await host.CreateBundleAsync("/tmp/clone", "/tmp/out/fix.bundle",
            new BranchName("main"), new BranchName("rix/fix"), CancellationToken.None);

        Assert.IsTrue(captured, "git runner should have been invoked");
        Assert.IsNull(capturedEnv, "local-only bundle must not receive the credential env");
    }

    [TestMethod]
    public async Task CreateBundleAsync_CallsGitBundle_InRepoDirectory_WithCorrectArgs()
    {
        string[]? capturedArgs = null;
        string? capturedWorkingDir = null;
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, args, workingDir, _, _, _) =>
            {
                capturedArgs = args.ToArray();
                capturedWorkingDir = workingDir;
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await host.CreateBundleAsync("/tmp/clone", "/tmp/out/fix.bundle",
            new BranchName("main"), new BranchName("rix/fix"), CancellationToken.None);

        Assert.IsNotNull(capturedArgs);
        Assert.AreEqual("/tmp/clone", capturedWorkingDir);
        CollectionAssert.AreEqual(ExpectedBundleArgs, capturedArgs);
    }

    [TestMethod]
    public async Task PushBranchAsync_RunsGitPush_InRepoDir_WithAuthEnv()
    {
        string[]? capturedArgs = null;
        string? capturedWorkingDir = null;
        IReadOnlyDictionary<string, string>? capturedEnv = null;
        var host = BuildWriteHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, args, workingDir, env, _, _) =>
            {
                capturedArgs = args.ToArray();
                capturedWorkingDir = workingDir;
                capturedEnv = env;
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await host.PushBranchAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None);

        CollectionAssert.AreEqual(ExpectedPushArgs, capturedArgs);
        Assert.AreEqual("/tmp/clone", capturedWorkingDir);
        Assert.IsNotNull(capturedEnv);
        Assert.AreEqual("1", capturedEnv["GIT_CONFIG_COUNT"]);
    }

    [TestMethod]
    public async Task PushBranchAsync_Throws_WhenGitFails()
    {
        var host = BuildWriteHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.PushBranchAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "push");
    }

    [TestMethod]
    public async Task CreateBundleAsync_Throws_WhenGitFails()
    {
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.CreateBundleAsync("/tmp/clone", "/tmp/out/fix.bundle",
                new BranchName("main"), new BranchName("rix/fix"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "bundle");
    }

    [TestMethod]
    public async Task ConfigureGitAsync_SetsUserName_ThenEmail_InRepoDirectory_NoAuthEnv()
    {
        var runs = new List<(string[] Args, string WorkingDir, IReadOnlyDictionary<string, string>? Env)>();
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, args, workingDir, env, _, _) =>
            {
                runs.Add((args.ToArray(), workingDir, env));
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await host.ConfigureGitAsync("/tmp/clone", CancellationToken.None);

        Assert.AreEqual(2, runs.Count);
        CollectionAssert.AreEqual(new[] { "config", "user.name", "rix" }, runs[0].Args);
        CollectionAssert.AreEqual(new[] { "config", "user.email", "rix@noreply.invalid" }, runs[1].Args);
        Assert.AreEqual("/tmp/clone", runs[0].WorkingDir);
        Assert.AreEqual("/tmp/clone", runs[1].WorkingDir);
        Assert.IsNull(runs[0].Env, "local-only config must not receive the credential env");
        Assert.IsNull(runs[1].Env, "local-only config must not receive the credential env");
    }

    [TestMethod]
    public async Task ConfigureGitAsync_Throws_WhenGitFails()
    {
        var host = BuildHost(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => host.ConfigureGitAsync("/tmp/clone", CancellationToken.None));
        StringAssert.Contains(ex.Message, "config");
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
    public async Task CreatePullRequestAsync_PostsToPullsEndpoint_AndReturnsHtmlUrl()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var host = BuildWriteHost(req =>
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
        var host = BuildWriteHost(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => host.CreatePullRequestAsync(SamplePr("t", "b"), CancellationToken.None));
    }

    [TestMethod]
    public async Task CreatePullRequestAsync_Throws_WhenResponseIsNotParseableJson()
    {
        var host = BuildWriteHost(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("not json"),
        });

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => host.CreatePullRequestAsync(SamplePr("t", "b"), CancellationToken.None));
    }

    private static PendingPr SamplePr(string title, string body)
    => new
    (
        new RixBranchName("rix/fix"), new BranchName("main"),
        new PrTitle(title), new PrBody(body), "rix_2Ffix.bundle"
    );

    private static GitHubHost BuildWriteHost(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string repo = "owner/repo",
        string writeToken = "write-tok",
        RunProcessAsync? gitRunner = null)
    => new
    (
        TestConfig.Repo(repo),
        new GitToken(writeToken),
        gitRunner ?? SuccessGitRunner,
        new DelegatingHandlerStub(handler)
    );

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        => Task.FromResult(handler(request));
    }
}
