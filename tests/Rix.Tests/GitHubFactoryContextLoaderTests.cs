using Rix.Process;
using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class GitHubFactoryContextLoaderTests
{
    private string _workDir = null!;
    private string _homeDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _workDir = Directory.CreateTempSubdirectory("rix-factory-work-").FullName;
        _homeDir = Directory.CreateTempSubdirectory("rix-factory-home-").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch (DirectoryNotFoundException) { }
        try { Directory.Delete(_homeDir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private GitHubFactoryContextLoader Loader(RunProcessAsync runProcess)
    => new(new GitReadToken("tok"), runProcess, _workDir, _homeDir);

    /// <summary>A fake <c>git</c> that mimics a sparse clone by writing <paramref name="tree"/>
    /// (relative path → contents) plus any <paramref name="emptyDirs"/> under the requested context
    /// directory of the clone target, and records every invocation's args and env.</summary>
    private static RunProcessAsync FakeGit(
        string contextDir,
        IReadOnlyDictionary<string, string> tree,
        IEnumerable<string>? emptyDirs = null,
        List<(string[] Args, IReadOnlyDictionary<string, string>? Env)>? calls = null)
    => (file, args, wd, env, onLine, ct) =>
    {
        var a = args.ToArray();
        calls?.Add((a, env));
        if (a[0] == "clone")
        {
            var dest = a[^1];
            foreach (var (rel, contents) in tree)
            {
                var target = System.IO.Path.Combine(dest, contextDir, rel);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.WriteAllText(target, contents);
            }
            foreach (var dir in emptyDirs ?? [])
                Directory.CreateDirectory(System.IO.Path.Combine(dest, contextDir, dir));
        }
        return Task.FromResult<ProcessResult>(new ProcessSuccess());
    };

    [TestMethod]
    public async Task LoadAsync_CopiesFactoryTreeIntoHome()
    {
        var git = FakeGit("agent-home", new Dictionary<string, string>
        {
            ["a.txt"] = "from-factory",
            ["nested/b.txt"] = "b",
        }, emptyDirs: ["placeholder"]);

        await Loader(git).LoadAsync(TestConfig.Repo("acme/factory"), TestConfig.RelPath("agent-home"), CancellationToken.None);

        Assert.AreEqual("from-factory", await File.ReadAllTextAsync(System.IO.Path.Combine(_homeDir, "a.txt")));
        Assert.AreEqual("b", await File.ReadAllTextAsync(System.IO.Path.Combine(_homeDir, "nested", "b.txt")));
        Assert.IsTrue(Directory.Exists(System.IO.Path.Combine(_homeDir, "placeholder")), "empty directories are preserved");
    }

    [TestMethod]
    public async Task LoadAsync_LeavesExistingHomeFilesUntouched()
    {
        await File.WriteAllTextAsync(System.IO.Path.Combine(_homeDir, "a.txt"), "original");
        var git = FakeGit("agent-home", new Dictionary<string, string>
        {
            ["a.txt"] = "from-factory",
            ["b.txt"] = "new",
        });

        await Loader(git).LoadAsync(TestConfig.Repo("acme/factory"), TestConfig.RelPath("agent-home"), CancellationToken.None);

        Assert.AreEqual("original", await File.ReadAllTextAsync(System.IO.Path.Combine(_homeDir, "a.txt")));
        Assert.AreEqual("new", await File.ReadAllTextAsync(System.IO.Path.Combine(_homeDir, "b.txt")));
    }

    [TestMethod]
    public async Task LoadAsync_SparseChecksOutTheContextPath_WithAuthOnlyOnClone()
    {
        var calls = new List<(string[] Args, IReadOnlyDictionary<string, string>? Env)>();
        var git = FakeGit("nested/agent-home", new Dictionary<string, string> { ["a.txt"] = "x" }, calls: calls);

        await Loader(git).LoadAsync(TestConfig.Repo("acme/factory"), TestConfig.RelPath("nested/agent-home"), CancellationToken.None);

        var clone = calls.Single(c => c.Args[0] == "clone");
        CollectionAssert.Contains(clone.Args, "--sparse");
        CollectionAssert.Contains(clone.Args, "--filter=blob:none");
        Assert.IsTrue(clone.Env?.ContainsKey("GIT_CONFIG_VALUE_0"), "clone must carry the auth extraheader env");

        var sparse = calls.Single(c => c.Args.Contains("sparse-checkout"));
        string[] expectedSparseArgs = ["sparse-checkout", "set", "nested/agent-home"];
        CollectionAssert.IsSubsetOf(expectedSparseArgs, sparse.Args);
        Assert.IsNull(sparse.Env, "local sparse-checkout must not be handed the credential env");
    }

    [TestMethod]
    public async Task LoadAsync_Throws_WhenContextPathAbsentFromRepo()
    {
        var git = FakeGit("some-other-dir", new Dictionary<string, string> { ["a.txt"] = "x" });

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Loader(git).LoadAsync(TestConfig.Repo("acme/factory"), TestConfig.RelPath("agent-home"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "factory context path not found");
    }

    [TestMethod]
    public async Task LoadAsync_Throws_WhenGitCloneFails()
    {
        RunProcessAsync git = (file, args, wd, env, onLine, ct)
            => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 128"));

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Loader(git).LoadAsync(TestConfig.Repo("acme/factory"), TestConfig.RelPath("agent-home"), CancellationToken.None));
        StringAssert.Contains(ex.Message, "git clone failed");
    }
}
