using Rix.Initialize;
using System.Reflection;

namespace Rix.Tests;

/// <summary>Covers the ref that ends up after the <c>@</c> of the written workflows' <c>uses:</c>
/// lines: what a caller is allowed to pin to, and what rix pins to on its own.</summary>
[TestClass]
public class WorkflowRefTests
{
    [TestMethod]
    [DataRow("v0")]
    [DataRow("v1.2.3")]
    [DataRow("main")]
    [DataRow("release/2.x")]
    [DataRow("10f8ca55a5edface0e59d9bc19e60664ad917024")]
    public void Constructor_AcceptsTagsBranchesAndShas(string value)
    => Assert.AreEqual(value, new WorkflowRef(value).Value);

    [TestMethod]
    [DataRow("")]
    [DataRow("v1 v2")]
    [DataRow("'main'")]
    [DataRow("-main")]
    [DataRow("main\nrun: rm -rf /")]
    public void Constructor_RejectsAnythingThatWouldNotSurviveYaml(string value)
    {
        var error = Assert.ThrowsExactly<InvalidInputException>(() => new WorkflowRef(value)).Message;
        StringAssert.Contains(error, "not a valid git ref");
    }

    [TestMethod]
    public void ForThisBuild_IsTheMajorVersionTag_OfThisBinarysRelease()
    {
        // Read from the assembly's own version rather than the informational one the property
        // parses, so a change to how that string is split shows up here as a mismatch.
        var major = typeof(WorkflowRef).Assembly.GetName().Version?.Major;
        Assert.AreEqual($"v{major}", WorkflowRef.ForThisBuild.Value);
    }
}
