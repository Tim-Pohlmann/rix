using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rix;

[JsonConverter(typeof(BranchNameJsonConverter))]
internal record BranchName(string Value)
{
    /// <summary>Parses raw text into a <see cref="BranchName"/>. Currently every non-null string is
    /// accepted, but the boundary exists so future format rules can return a
    /// <see cref="ParseError{T}"/> alongside the other value objects rather than throwing.</summary>
    internal static ParseResult<BranchName> Parse(string value) =>
        new ParseSuccess<BranchName>(new BranchName(value));

    public override string ToString() => Value;
}

[JsonConverter(typeof(RixBranchNameJsonConverter))]
internal record RixBranchName : BranchName
{
    private static readonly Regex Pattern =
        new(@"^rix/.+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    internal RixBranchName(string value) : base(value)
    {
        if (!Pattern.IsMatch(value))
            throw new ArgumentException($"Branch must match rix/* pattern, got: {value}", nameof(value));
    }

    /// <summary>The single source of truth for the <c>rix/*</c> rule on the union path: returns a
    /// <see cref="ParseError{T}"/> for malformed input so callers can aggregate it instead of
    /// catching an exception.</summary>
    internal static new ParseResult<RixBranchName> Parse(string value) =>
        Pattern.IsMatch(value)
            ? new ParseSuccess<RixBranchName>(new RixBranchName(value))
            : new ParseError<RixBranchName>($"Branch must match rix/* pattern, got: {value}");
}

internal sealed class BranchNameJsonConverter : StringValueJsonConverter<BranchName>
{
    protected override ParseResult<BranchName> Parse(string value) => BranchName.Parse(value);
    protected override string Extract(BranchName value) => value.Value;
}

internal sealed class RixBranchNameJsonConverter : StringValueJsonConverter<RixBranchName>
{
    protected override ParseResult<RixBranchName> Parse(string value) => RixBranchName.Parse(value);
    protected override string Extract(RixBranchName value) => value.Value;
}
