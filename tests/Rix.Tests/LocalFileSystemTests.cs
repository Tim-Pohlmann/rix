namespace Rix.Tests;

/// <summary>Covers the one thing <see cref="LocalFileSystem"/> decides rather than forwards: where
/// a private temp directory goes and who may look inside it. Everything else on the type is a
/// one-line delegation to <c>File</c>/<c>Directory</c>, exercised through the code that uses it.</summary>
[TestClass]
public class LocalFileSystemTests
{
    private readonly List<string> _created = [];

    private string CreatePrivateTempDirectory()
    {
        var path = new LocalFileSystem().CreatePrivateTempDirectory();
        _created.Add(path);
        return path;
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in _created)
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) { /* best-effort, same as TempDirectory */ }
    }

    [TestMethod]
    public void CreatePrivateTempDirectory_CreatesTheDirectory()
    {
        var path = CreatePrivateTempDirectory();

        Assert.IsTrue(Directory.Exists(path));
        Assert.AreEqual(Path.GetFullPath(path), path, "the path must be absolute, so callers combining onto it stay rooted");
    }

    [TestMethod]
    public void CreatePrivateTempDirectory_ReturnsAFreshDirectoryEachCall()
    => Assert.AreNotEqual(CreatePrivateTempDirectory(), CreatePrivateTempDirectory());

    /// <summary>The reason this method exists instead of a path under the shared temp root: what
    /// rix puts in the work dir is a clone of the target repo and the agent's workspace, and on a
    /// multi-user machine the shared root would leave both readable by everyone.</summary>
    [TestMethod]
    public void CreatePrivateTempDirectory_IsReadableOnlyByItsOwner()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix permission bits; Windows relies on the inherited ACL of the user's own temp.");
            return;
        }

        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.AreEqual(expected, File.GetUnixFileMode(CreatePrivateTempDirectory()));
    }
}
