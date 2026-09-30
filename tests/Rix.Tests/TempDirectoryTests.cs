namespace Rix.Tests;

[TestClass]
public class TempDirectoryTests
{
    private string _baseDir = null!;

    [TestInitialize]
    public void Setup()
    => _baseDir = Directory.CreateTempSubdirectory("rix-temp-base-").FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_baseDir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    [TestMethod]
    public void CreatesAPrefixedDirectoryUnderTheBase_AndDeletesItOnDispose()
    {
        string path;
        using (var temp = TempDirectory.Create(new LocalFileSystem(), _baseDir, "rix-test"))
        {
            path = temp.Path;
            File.WriteAllText(Path.Combine(path, "a.txt"), "a");
            Assert.AreEqual(_baseDir, Path.GetDirectoryName(path));
            StringAssert.StartsWith(Path.GetFileName(path), "rix-test-");
        }

        Assert.IsFalse(Directory.Exists(path));
    }

    [TestMethod]
    public void Dispose_SwallowsCleanupFailures()
    {
        var temp = TempDirectory.Create
        (
            new StubFileSystem(deleteDirectory: _ => throw new UnauthorizedAccessException("denied")), _baseDir, "rix-test"
        );

        temp.Dispose();

        Assert.IsTrue(Directory.Exists(temp.Path), "a failed cleanup leaves the directory behind rather than faulting");
    }
}
