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
        var error = Assert.ThrowsExactly<InvalidInputException>(() => new WorkflowRef(value).ToString()).Message;
        StringAssert.Contains(error, "not a valid git ref");
    }

    [TestMethod]
    [DataRow("0.5.0+10f8ca55a5edface0e59d9bc19e60664ad917024", "0")]
    [DataRow("12.1.0", "12")]
    public void MajorOf_TakesTheMajor_OffTheStampedInformationalVersion(string informationalVersion, string expected)
    => Assert.AreEqual(expected, WorkflowRef.MajorOf(informationalVersion));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("x.1.0")]
    public void MajorOf_RejectsAVersionThisBuildCouldNotHaveProduced(string? informationalVersion)
    {
        // A broken build, not caller input: it surfaces as an InvalidOperationException rather than
        // the InvalidInputException a bad --ref raises, so it is never reported as the user's fault.
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => WorkflowRef.MajorOf(informationalVersion)).Message;
        StringAssert.Contains(error, "not a semantic version");
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
