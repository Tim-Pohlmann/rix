using Rix.Repository;

namespace Rix.Tests;

[TestClass]
public class AgentHomeFetcherTests
{
    private string _checkoutDir = null!;

    [TestInitialize]
    public void Setup()
    => _checkoutDir = Directory.CreateTempSubdirectory("rix-agent-home-checkout-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_checkoutDir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private Task<DirectoryPath> Fetch(IGit git, string sourcePath = "agent-home", Action<RepoIdentifier>? onGitFor = null)
    => new AgentHomeFetcher(repo =>
        {
            onGitFor?.Invoke(repo);
            return git;
        })
        .FetchAsync(new RepoIdentifier("acme/factory"), new SubDirectoryPath(sourcePath), new DirectoryPath(_checkoutDir), CancellationToken.None);

    /// <summary>A git that mimics a sparse clone by creating <paramref name="contextDir"/> with one
    /// file in the clone target, and records the directory it was asked to check out.</summary>
    private static StubGit FakeGit(string contextDir, List<SubDirectoryPath>? requested = null)
    => new(sparseClone: (target, directory) =>
    {
        requested?.Add(directory);
        var root = Path.Combine(target, contextDir);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.txt"), "x");
        return Task.CompletedTask;
    });

    [TestMethod]
    public async Task FetchAsync_ReturnsTheContextDirectory_InsideTheCheckout()
    {
        var source = await Fetch(FakeGit("nested/agent-home"), "nested/agent-home");

        Assert.AreEqual(new DirectoryPath(Path.Combine(_checkoutDir, "nested/agent-home")), source);
        Assert.IsTrue(File.Exists(Path.Combine(source.Value, "a.txt")));
    }

    [TestMethod]
    public async Task FetchAsync_SparseClonesTheSourcePath_OfTheFactoryRepo()
    {
        var requested = new List<SubDirectoryPath>();
        RepoIdentifier? repo = null;

        await Fetch(FakeGit("nested/agent-home", requested), "nested/agent-home", r => repo = r);

        Assert.AreEqual(new RepoIdentifier("acme/factory"), repo);
        Assert.AreEqual(new SubDirectoryPath("nested/agent-home"), requested.Single());
    }

    [TestMethod]
    public async Task FetchAsync_Throws_WhenSourcePathAbsentFromRepo()
    {
        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(() => Fetch(FakeGit("some-other-dir")));
        StringAssert.Contains(ex.Message, "agent home path not found");
    }

    [TestMethod]
    public async Task FetchAsync_Throws_WhenGitCloneFails()
    {
        var git = new StubGit(sparseClone: (_, _) => throw new RepoHostException("git clone failed: exited with code 128"));

        var ex = await Assert.ThrowsExactlyAsync<RepoHostException>(() => Fetch(git));
        StringAssert.Contains(ex.Message, "git clone failed");
    }
}
