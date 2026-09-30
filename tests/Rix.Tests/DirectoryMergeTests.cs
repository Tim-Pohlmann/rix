namespace Rix.Tests;

[TestClass]
public class DirectoryMergeTests
{
    private string _source = null!;
    private string _dest = null!;

    [TestInitialize]
    public void Setup()
    {
        _source = Directory.CreateTempSubdirectory("rix-merge-src-").FullName;
        _dest = Directory.CreateTempSubdirectory("rix-merge-dest-").FullName;
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_source, recursive: true); } catch (DirectoryNotFoundException) { }
        try { Directory.Delete(_dest, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private string Src(params string[] parts) => Path.Combine([_source, .. parts]);
    private string Dest(params string[] parts) => Path.Combine([_dest, .. parts]);

    private void Merge() => DirectoryMerge.CopySkippingExisting(new LocalFileSystem(), _source, _dest);

    [TestMethod]
    public void CopiesFilesAndDirectories_IncludingEmptyOnes()
    {
        File.WriteAllText(Src("a.txt"), "a");
        Directory.CreateDirectory(Src("nested"));
        File.WriteAllText(Src("nested", "b.txt"), "b");
        Directory.CreateDirectory(Src("placeholder"));

        Merge();

        Assert.AreEqual("a", File.ReadAllText(Dest("a.txt")));
        Assert.AreEqual("b", File.ReadAllText(Dest("nested", "b.txt")));
        Assert.IsTrue(Directory.Exists(Dest("placeholder")), "empty directories are preserved");
    }

    [TestMethod]
    public void KeepsExistingFiles_AndMergesIntoExistingDirectories()
    {
        File.WriteAllText(Dest("a.txt"), "original");
        Directory.CreateDirectory(Dest("nested"));
        File.WriteAllText(Src("a.txt"), "from-source");
        Directory.CreateDirectory(Src("nested"));
        File.WriteAllText(Src("nested", "b.txt"), "new");

        Merge();

        Assert.AreEqual("original", File.ReadAllText(Dest("a.txt")));
        Assert.AreEqual("new", File.ReadAllText(Dest("nested", "b.txt")));
    }

    [TestMethod]
    public void KeepsWhateverAlreadyOccupiesATarget_FileOrDirectory()
    {
        Directory.CreateDirectory(Dest("was-dir"));
        File.WriteAllText(Dest("was-file"), "original");
        File.WriteAllText(Src("was-dir"), "file where the destination has a directory");
        Directory.CreateDirectory(Src("was-file"));
        File.WriteAllText(Src("was-file", "inner.txt"), "directory where the destination has a file");

        Merge();

        Assert.IsTrue(Directory.Exists(Dest("was-dir")));
        Assert.AreEqual("original", File.ReadAllText(Dest("was-file")));
    }

    [TestMethod]
    public void KeepsAnExistingDanglingSymlink()
    {
        File.CreateSymbolicLink(Dest("a.txt"), "missing-target");
        File.WriteAllText(Src("a.txt"), "from-source");

        Merge();

        Assert.AreEqual("missing-target", new FileInfo(Dest("a.txt")).LinkTarget);
        Assert.IsFalse(File.Exists(Dest("missing-target")), "the source file must not be written through the link");
    }

    [TestMethod]
    public void RecreatesSymlinks_InsteadOfFollowingThem()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"rix-merge-outside-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "runner secret");
        try
        {
            // Followed, this loop would recurse until the path got too long.
            File.CreateSymbolicLink(Src("self"), "..");
            // Followed, this would copy a file from elsewhere on the runner into the destination.
            File.CreateSymbolicLink(Src("abs"), outside);

            Merge();

            Assert.AreEqual("..", new FileInfo(Dest("self")).LinkTarget);
            Assert.AreEqual(outside, new FileInfo(Dest("abs")).LinkTarget);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [TestMethod]
    public void Throws_WhenTheDestinationCannotBeCreated()
    {
        File.WriteAllText(Src("a.txt"), "x");
        var notADir = Dest("not-a-dir");
        File.WriteAllText(notADir, "x");

        Assert.ThrowsExactly<IOException>(() => DirectoryMerge.CopySkippingExisting(new LocalFileSystem(), _source, notADir));
    }
}
