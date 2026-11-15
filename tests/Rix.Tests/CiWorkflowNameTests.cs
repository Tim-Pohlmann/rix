using Rix.Initialize;

namespace Rix.Tests;

/// <summary>Covers the workflow name the written <c>rix-on-ci-failure.yml</c> watches, which lands
/// inside a double-quoted YAML string.</summary>
[TestClass]
public class CiWorkflowNameTests
{
    [TestMethod]
    [DataRow("CI")]
    [DataRow("Build and test")]
    [DataRow("ci: main")]
    [DataRow("Tests (linux)")]
    public void Constructor_AcceptsWorkflowNames(string value)
    => Assert.AreEqual(value, new CiWorkflowName(value).Value);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("say \"hi\"")]
    [DataRow(@"a\nb")]
    [DataRow("CI\n")]
    [DataRow("CI\"]\n  branches: [\"x")]
    public void Constructor_RejectsAnythingThatWouldNotSurviveADoubleQuotedString(string value)
    {
        var error = Assert.ThrowsExactly<InvalidInputException>(() => new CiWorkflowName(value).ToString()).Message;
        StringAssert.Contains(error, "is not a workflow name rix can write");
    }

    [TestMethod]
    public void Default_IsCI()
    => Assert.AreEqual("CI", CiWorkflowName.Default.Value);
}
