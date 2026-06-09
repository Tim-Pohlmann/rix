using Rix.Job;

namespace Rix.Tests;

[TestClass]
public class BundleFileTests
{
    [TestMethod]
    public void ForBranch_AppendsBundleExtension()
    {
        Assert.AreEqual("rix_2Fmy-fix.bundle", BundleFile.ForBranch(new RixBranchName("rix/my-fix")));
    }

    [TestMethod]
    public void ForBranch_EncodesSlashesAsSafeSegment()
    {
        Assert.AreEqual("rix_2Ffeat_2Fsub.bundle", BundleFile.ForBranch(new RixBranchName("rix/feat/sub")));
    }
}
