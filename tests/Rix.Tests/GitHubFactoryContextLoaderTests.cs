using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class GitHubFactoryContextLoaderTests
{
    private string _checkoutDir = null!;

    [TestInitialize]
    public void Setup()
    => _checkoutDir = Directory.CreateTempSubdirectory("rix-factory-checkout-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_checkoutDir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private Task<string> Fetch(RunProcessAsync git, string contextPath = "agent-home")
    => new GitHubFactoryContextLoader(new GitCli(new GitReadToken("tok"), git))
        .FetchAsync(new RepoIdentifier("acme/factory"), new RepoRelativePath(contextPath), _checkoutDir, CancellationToken.None);

    /// <summary>A fake <c>git</c> that mimics a sparse clone by creating <paramref name="contextDir"/>
    /// with one file in the clone target, and records every invocation's args and env.</summary>
    private static RunProcessAsync FakeGit
    (
        string contextDir,
        List<(string[] Args, IReadOnlyDictionary<string, string>? Env)>? calls = null
    )
    => (file, args, wd, env, onLine, ct) =>
    {
        var a = args.ToArray();
        calls?.Add((a, env));
        if (a[0] == "clone")
        {
            var root = Path.Combine(a[^1], contextDir);
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        }
        return Task.FromResult<ProcessResult>(new ProcessSuccess());
    };

    [TestMethod]
    public async Task FetchAsync_ReturnsTheContextDirectory_InsideTheCheckout()
    {
        var source = await Fetch(FakeGit("nested/agent-home"), "nested/agent-home");

        Assert.AreEqual(Path.Combine(_checkoutDir, "nested/agent-home"), source);
        Assert.IsTrue(File.Exists(Path.Combine(source, "a.txt")));
    }

    [TestMethod]
    public async Task FetchAsync_SparseChecksOutTheContextPath_WithAuthOnBothSteps()
    {
        var calls = new List<(string[] Args, IReadOnlyDictionary<string, string>? Env)>();

        await Fetch(FakeGit("nested/agent-home", calls), "nested/agent-home");

        var clone = calls.Single(c => c.Args[0] == "clone");
        CollectionAssert.Contains(clone.Args, "--sparse");
        CollectionAssert.Contains(clone.Args, "--filter=blob:none");
        Assert.IsTrue(clone.Env?.ContainsKey("GIT_CONFIG_VALUE_0"), "clone must carry the auth extraheader env");

        var sparse = calls.Single(c => c.Args[0] == "sparse-checkout");
        string[] expectedSparseArgs = ["sparse-checkout", "set", "nested/agent-home"];
        CollectionAssert.AreEqual(expectedSparseArgs, sparse.Args);
        // The clone is blobless, so sparse-checkout fetches the context files from GitHub: without
        // the credential, a private factory repo fails right here.
        Assert.IsTrue(sparse.Env?.ContainsKey("GIT_CONFIG_VALUE_0"), "sparse-checkout fetches blobs, so it must carry the auth extraheader env");
    }

    [TestMethod]
    public async Task FetchAsync_Throws_WhenContextPathAbsentFromRepo()
    {
        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(() => Fetch(FakeGit("some-other-dir")));
        StringAssert.Contains(ex.Message, "factory context path not found");
    }

    [TestMethod]
    public async Task FetchAsync_Throws_WhenGitCloneFails()
    {
        RunProcessAsync git = (file, args, wd, env, onLine, ct)
            => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128"));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(() => Fetch(git));
        StringAssert.Contains(ex.Message, "git clone failed");
    }
}
