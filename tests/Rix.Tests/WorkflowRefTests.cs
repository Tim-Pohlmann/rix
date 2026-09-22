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
    // Nobody would pin to this, but git's trailing-dot rule is about the refname as a whole, so a
    // dot at the end of an interior component is legal. Kept as a row to pin down that the
    // rejection below is the one git makes and not a per-component rule that only looks similar.
    [DataRow("release./2.x")]
    public void Constructor_AcceptsTagsBranchesAndShas(string value)
    => Assert.AreEqual(value, new WorkflowRef(value).Value);

    [TestMethod]
    [DataRow("")]
    [DataRow("v1 v2")]
    [DataRow("'main'")]
    [DataRow("-main")]
    [DataRow("main\nrun: rm -rf /")]
    // A newline with nothing after it, which the interior-newline row above does not cover: .NET's
    // $ matches immediately before a final newline, so this rode through validation and into the
    // uses: line until the pattern was anchored with \z.
    [DataRow("main\n")]
    public void Constructor_RejectsAnythingThatWouldNotSurviveYaml(string value)
    {
        var error = Assert.ThrowsExactly<InvalidInputException>(() => new WorkflowRef(value).ToString()).Message;
        StringAssert.Contains(error, "not a valid workflow ref");
    }

    /// <summary>Shapes that survive YAML intact but that git itself refuses, so pinning to one
    /// would write a workflow whose <c>uses:</c> line cannot resolve — a failure that surfaces in
    /// the target repo's Actions tab, a commit and a push away from the mistake.</summary>
    [TestMethod]
    [DataRow("release..x")]
    [DataRow("release/")]
    [DataRow("release/.hidden")]
    [DataRow("release//2.x")]
    [DataRow("v1.lock")]
    [DataRow("release/2.x.lock")]
    [DataRow("main.")]
    [DataRow("release/2.x.")]
    public void Constructor_RejectsRefsGitWouldReject(string value)
    {
        var error = Assert.ThrowsExactly<InvalidInputException>(() => new WorkflowRef(value).ToString()).Message;
        StringAssert.Contains(error, "not a valid workflow ref");
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
