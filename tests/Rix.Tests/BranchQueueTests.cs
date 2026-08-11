using Rix.Api;

namespace Rix.Tests;

[TestClass]
public class BranchQueueTests
{
    [TestMethod]
    public void TryAdd_RejectsSecondAddForSameBranch()
    {
        var queue = new BranchQueue<string>();

        Assert.IsTrue(queue.TryAdd("rix/a", "first"));
        Assert.IsFalse(queue.TryAdd("rix/a", "second"));
        CollectionAssert.AreEqual(new[] { "first" }, queue.Snapshot());
    }

    [TestMethod]
    public void Snapshot_PreservesInsertionOrder()
    {
        var queue = new BranchQueue<string>();

        Assert.IsTrue(queue.TryAdd("rix/c", "c"));
        Assert.IsTrue(queue.TryAdd("rix/a", "a"));
        Assert.IsTrue(queue.TryAdd("rix/b", "b"));

        CollectionAssert.AreEqual(new[] { "c", "a", "b" }, queue.Snapshot());
    }

    [TestMethod]
    public void Snapshot_OmitsRemovedBranches()
    {
        var queue = new BranchQueue<string>();
        queue.TryAdd("rix/a", "a");
        queue.TryAdd("rix/b", "b");

        Assert.IsTrue(queue.TryRemove("rix/a"));

        CollectionAssert.AreEqual(new[] { "b" }, queue.Snapshot());
    }

    [TestMethod]
    public void ReAddingAfterRemoval_MovesBranchToTheEnd()
    {
        var queue = new BranchQueue<string>();
        queue.TryAdd("rix/a", "a1");
        queue.TryAdd("rix/b", "b");
        queue.TryRemove("rix/a");

        Assert.IsTrue(queue.TryAdd("rix/a", "a2"));

        CollectionAssert.AreEqual(new[] { "b", "a2" }, queue.Snapshot());
    }

    [TestMethod]
    public void TryRemove_ReturnsFalse_WhenBranchNotQueued()
    {
        var queue = new BranchQueue<string>();

        Assert.IsFalse(queue.TryRemove("rix/missing"));
    }
}
