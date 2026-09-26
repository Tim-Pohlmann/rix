using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class GitCliTests
{
    private static readonly RunProcessAsync SuccessGitRunner =
        (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessSuccess());

    private static readonly string[] ExpectedBundleArgs =
        ["bundle", "create", "/tmp/out/fix.bundle", "--end-of-options", "main..rix/fix"];

    private static readonly string[] ExpectedPushArgs = ["push", "origin", "--end-of-options", "rix/fix"];

    private static readonly string[] ExpectedBranchExistsLocallyArgs =
        ["rev-parse", "--verify", "--quiet", "refs/heads/rix/fix"];

    private static readonly string[] ExpectedConfigureUserNameArgs = ["config", "user.name", "rix"];

    private static readonly string[] ExpectedConfigureUserEmailArgs =
        ["config", "user.email", "rix@noreply.invalid"];

    private static readonly string[] ExpectedLsRemoteArgs =
        ["ls-remote", "--exit-code", "https://github.com/owner/repo.git", "refs/heads/rix/fix"];

    private static readonly string[] ExpectedSparseCloneArgs =
        ["clone", "--depth", "1", "--filter=blob:none", "--sparse", "https://github.com/owner/repo.git", "/tmp/checkout"];

    private static readonly string[] ExpectedSparseCheckoutArgs = ["sparse-checkout", "set", "nested/agent-home"];

    private static GitCli Build(string readToken = "read-tok", RunProcessAsync? gitRunner = null)
    => new(new Uri("https://github.com/owner/repo.git"), new GitReadToken(readToken), gitRunner ?? SuccessGitRunner);

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_RunsLsRemote_AgainstTheRemote_WithAuthEnv()
    {
        string[]? capturedArgs = null;
        IReadOnlyDictionary<string, string>? capturedEnv = null;
        var git = Build(
            gitRunner: (_, args, _, env, onLine, _) =>
            {
                capturedArgs = args.ToArray();
                capturedEnv = env;
                onLine?.Invoke("0123abcd\trefs/heads/rix/fix");
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        Assert.IsTrue(await git.BranchExistsOnRemoteAsync(new BranchName("rix/fix"), CancellationToken.None));
        CollectionAssert.AreEqual(ExpectedLsRemoteArgs, capturedArgs);
        Assert.IsNotNull(capturedEnv);
        Assert.AreEqual("1", capturedEnv["GIT_CONFIG_COUNT"]);
    }

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_ReturnsFalse_WhenNothingMatches()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 2")));

        Assert.IsFalse(await git.BranchExistsOnRemoteAsync(new BranchName("rix/missing"), CancellationToken.None));
    }

    /// <summary>ls-remote patterns are globs, so a branch named with a <c>*</c> lists other
    /// branches and exits 0; only an exact ref counts.</summary>
    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_ReturnsFalse_WhenOnlyGlobMatchesAreListed()
    {
        var git = Build(
            gitRunner: (_, _, _, _, onLine, _) =>
            {
                onLine?.Invoke("0123abcd\trefs/heads/rix/fix");
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        Assert.IsFalse(await git.BranchExistsOnRemoteAsync(new BranchName("rix/*"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsOnRemoteAsync_Throws_OnOperationalFailure()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => git.BranchExistsOnRemoteAsync(new BranchName("rix/fix"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "ls-remote");
    }

    [TestMethod]
    public async Task BranchExistsLocallyAsync_ReturnsTrue_WhenGitRevParseSucceeds()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessSuccess()));

        Assert.IsTrue(await git.BranchExistsLocallyAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsLocallyAsync_ReturnsFalse_WhenGitRevParseFails()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")));

        Assert.IsFalse(await git.BranchExistsLocallyAsync("/tmp/clone", new BranchName("rix/missing"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsLocallyAsync_Throws_OnOperationalFailure_NotJustMissingRef()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => git.BranchExistsLocallyAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None));
    }

    [TestMethod]
    public async Task BranchExistsLocallyAsync_RunsGitRevParse_InRepoDirectory_NoAuthEnv()
    {
        string[]? capturedArgs = null;
        string? capturedWorkingDir = null;
        IReadOnlyDictionary<string, string>? capturedEnv = null;
        var git = Build(
            gitRunner: (_, args, workingDir, env, _, _) =>
            {
                capturedArgs = args.ToArray();
                capturedWorkingDir = workingDir;
                capturedEnv = env;
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await git.BranchExistsLocallyAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None);

        CollectionAssert.AreEqual(ExpectedBranchExistsLocallyArgs, capturedArgs);
        Assert.AreEqual("/tmp/clone", capturedWorkingDir);
        Assert.IsNull(capturedEnv, "local-only check must not receive the credential env");
    }

    [TestMethod]
    public async Task CloneAsync_CallsGitClone_WithPlainUrl_NoTokenInArgs()
    {
        string[]? capturedArgs = null;
        var git = Build(
            readToken: "my-read-token",
            gitRunner: (_, args, _, _, _, _) => { capturedArgs = args.ToArray(); return Task.FromResult<ProcessResult>(new ProcessSuccess()); });

        await git.CloneAsync("/tmp/target", CancellationToken.None);

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
        var git = Build(
            readToken: "my-read-token",
            gitRunner: (_, _, _, env, _, _) => { capturedEnv = env; return Task.FromResult<ProcessResult>(new ProcessSuccess()); });

        await git.CloneAsync("/tmp/target", CancellationToken.None);

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
        var git = Build(
            gitRunner: (_, _, _, env, _, _) => { capturedEnv = env; captured = true; return Task.FromResult<ProcessResult>(new ProcessSuccess()); });

        await git.CreateBundleAsync("/tmp/clone", "/tmp/out/fix.bundle",
            new BranchName("main"), new BranchName("rix/fix"), CancellationToken.None);

        Assert.IsTrue(captured, "git runner should have been invoked");
        Assert.IsNull(capturedEnv, "local-only bundle must not receive the credential env");
    }

    [TestMethod]
    public async Task CreateBundleAsync_CallsGitBundle_InRepoDirectory_WithCorrectArgs()
    {
        string[]? capturedArgs = null;
        string? capturedWorkingDir = null;
        var git = Build(
            gitRunner: (_, args, workingDir, _, _, _) =>
            {
                capturedArgs = args.ToArray();
                capturedWorkingDir = workingDir;
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await git.CreateBundleAsync("/tmp/clone", "/tmp/out/fix.bundle",
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
        var git = Build(
            gitRunner: (_, args, workingDir, env, _, _) =>
            {
                capturedArgs = args.ToArray();
                capturedWorkingDir = workingDir;
                capturedEnv = env;
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await git.PushBranchAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None);

        CollectionAssert.AreEqual(ExpectedPushArgs, capturedArgs);
        Assert.AreEqual("/tmp/clone", capturedWorkingDir);
        Assert.IsNotNull(capturedEnv);
        Assert.AreEqual("1", capturedEnv["GIT_CONFIG_COUNT"]);
    }

    [TestMethod]
    public async Task PushBranchAsync_Throws_WhenGitFails()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => git.PushBranchAsync("/tmp/clone", new BranchName("rix/fix"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "push");
    }

    [TestMethod]
    public async Task CreateBundleAsync_Throws_WhenGitFails()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => git.CreateBundleAsync("/tmp/clone", "/tmp/out/fix.bundle",
                new BranchName("main"), new BranchName("rix/fix"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "bundle");
    }

    [TestMethod]
    public async Task ConfigureIdentityAsync_SetsUserName_ThenEmail_InRepoDirectory_NoAuthEnv()
    {
        var runs = new List<(string[] Args, string WorkingDir, IReadOnlyDictionary<string, string>? Env)>();
        var git = Build(
            gitRunner: (_, args, workingDir, env, _, _) =>
            {
                runs.Add((args.ToArray(), workingDir, env));
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await git.ConfigureIdentityAsync("/tmp/clone", CancellationToken.None);

        Assert.AreEqual(2, runs.Count);
        CollectionAssert.AreEqual(ExpectedConfigureUserNameArgs, runs[0].Args);
        CollectionAssert.AreEqual(ExpectedConfigureUserEmailArgs, runs[1].Args);
        Assert.AreEqual("/tmp/clone", runs[0].WorkingDir);
        Assert.AreEqual("/tmp/clone", runs[1].WorkingDir);
        Assert.IsNull(runs[0].Env, "local-only config must not receive the credential env");
        Assert.IsNull(runs[1].Env, "local-only config must not receive the credential env");
    }

    [TestMethod]
    public async Task ConfigureIdentityAsync_Throws_WhenGitFails()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => git.ConfigureIdentityAsync("/tmp/clone", CancellationToken.None));
        StringAssert.Contains(ex.Message, "config");
    }

    [TestMethod]
    public async Task CloneAsync_Throws_WhenGitFails()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => git.CloneAsync("/tmp/target", CancellationToken.None));
        StringAssert.Contains(ex.Message, "clone");
    }

    [TestMethod]
    public async Task SparseCloneAsync_ClonesShallowBloblessSparse_ThenChecksOutTheDirectory_WithAuthOnBothSteps()
    {
        var runs = new List<(string[] Args, string WorkingDir, IReadOnlyDictionary<string, string>? Env)>();
        var git = Build(
            gitRunner: (_, args, workingDir, env, _, _) =>
            {
                runs.Add((args.ToArray(), workingDir, env));
                return Task.FromResult<ProcessResult>(new ProcessSuccess());
            });

        await git.SparseCloneAsync("/tmp/checkout", new SubDirectoryPath("nested/agent-home"), CancellationToken.None);

        Assert.AreEqual(2, runs.Count);
        CollectionAssert.AreEqual(ExpectedSparseCloneArgs, runs[0].Args);
        CollectionAssert.AreEqual(ExpectedSparseCheckoutArgs, runs[1].Args);
        Assert.AreEqual("/tmp/checkout", runs[1].WorkingDir);
        Assert.IsTrue(runs[0].Env?.ContainsKey("GIT_CONFIG_VALUE_0"), "clone must carry the auth extraheader env");
        // The clone is blobless, so sparse-checkout fetches the files from the remote: without the
        // credential, a private repo fails right here.
        Assert.IsTrue(runs[1].Env?.ContainsKey("GIT_CONFIG_VALUE_0"), "sparse-checkout fetches blobs, so it must carry the auth extraheader env");
    }

    [TestMethod]
    public async Task SparseCloneAsync_Throws_WhenGitCloneFails()
    {
        var git = Build(
            gitRunner: (_, _, _, _, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128")));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(
            () => git.SparseCloneAsync("/tmp/checkout", new SubDirectoryPath("agent-home"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "git clone failed");
    }
}
