namespace Rix.Tests;

[TestClass]
public class InputTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Required_RejectsBlank_NamingTheInput(string? raw)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Input.Required("--repo", raw, value => value));
        Assert.AreEqual("--repo is required", ex.Message);
    }

    [TestMethod]
    public void Required_ConstructsFromNonBlank()
    => Assert.AreEqual("owner/repo", Input.Required("--repo", "owner/repo", value => new RepoIdentifier(value)).Value);

    [TestMethod]
    public void Required_PrefixesConstructorError_WithTheInputName()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Input.Required("--repo", "noslash", value => new RepoIdentifier(value)));
        StringAssert.StartsWith(ex.Message, "--repo: ");
        StringAssert.Contains(ex.Message, "noslash");
        Assert.IsInstanceOfType<InvalidInputException>(ex.InnerException);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Optional_FallsBack_WhenBlank(string? raw)
    => Assert.AreEqual(7, Input.Optional("--timeout", raw, Input.Positive<int>, 7));

    [TestMethod]
    public void Optional_ConstructsFromNonBlank()
    => Assert.AreEqual(5, Input.Optional("--timeout", "5", Input.Positive<int>, 7));

    [TestMethod]
    public void Optional_RejectsMalformed_RatherThanFallingBack()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Input.Optional("--timeout", "abc", Input.Positive<int>, 7));
        Assert.AreEqual("--timeout: must be a positive integer, got 'abc'", ex.Message);
    }

    [TestMethod]
    public void LazyOptional_NamesTheDefaultItFailedToBuild()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>
        (
            () => Input.Optional<string>("--work-dir", null, raw => raw, () => throw new InvalidInputException("directory does not exist: /tmp"))
        );

        Assert.AreEqual("--work-dir default: directory does not exist: /tmp", ex.Message);
    }

    [TestMethod]
    public void LazyOptional_BuildsTheFallbackOnlyWhenNothingWasSupplied()
    {
        var built = 0;

        Assert.AreEqual("given", Input.Optional("--work-dir", "given", raw => raw, () => { built++; return "default"; }));
        Assert.AreEqual(0, built);
        Assert.AreEqual("default", Input.Optional("--work-dir", null, raw => raw, () => { built++; return "default"; }));
        Assert.AreEqual(1, built);
    }

    [TestMethod]
    public void OptionalText_NormalisesBlankToNull()
    {
        Assert.IsNull(Input.OptionalText(null));
        Assert.IsNull(Input.OptionalText(""));
        Assert.IsNull(Input.OptionalText("  "));
        Assert.AreEqual("openai/gpt-4o", Input.OptionalText("openai/gpt-4o"));
    }

    [TestMethod]
    public void Named_PrefixesOnlyInvalidInput()
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Input.Named<string>("--agent-api-key-env", () => throw new InvalidInputException("is required")));
        Assert.AreEqual("--agent-api-key-env: is required", ex.Message);

        Assert.ThrowsExactly<InvalidOperationException>(() => Input.Named<string>("--x", () => throw new InvalidOperationException("boom")));
    }

    [TestMethod]
    [DataRow("abc")]
    [DataRow("0")]
    [DataRow("-5")]
    [DataRow("1.5")]
    public void Positive_RejectsNonPositiveOrNonNumeric(string raw)
    {
        var ex = Assert.ThrowsExactly<InvalidInputException>(() => Input.Positive<long>(raw));
        Assert.AreEqual($"must be a positive integer, got '{raw}'", ex.Message);
    }

    [TestMethod]
    public void Positive_ParsesPositiveNumbers()
    {
        Assert.AreEqual(42, Input.Positive<int>("42"));
        Assert.AreEqual(9_000_000_000L, Input.Positive<long>("9000000000"));
    }
}
