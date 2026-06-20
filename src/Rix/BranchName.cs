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
        if (Validate(value) is { } error)
            throw new ArgumentException(error, nameof(value));
    }

    /// <summary>Returns the union path's <see cref="RixBranchName"/> or, for malformed input, a
    /// <see cref="ParseError{T}"/> callers can aggregate instead of catching an exception.</summary>
    internal static new ParseResult<RixBranchName> Parse(string value) =>
        Validate(value) is { } error
            ? new ParseError<RixBranchName>(error)
            : new ParseSuccess<RixBranchName>(new RixBranchName(value));

    /// <summary>The single source of the <c>rix/*</c> rule and its message, shared by the throwing
    /// constructor and the non-throwing <see cref="Parse"/>: null when <paramref name="value"/> is
    /// valid, otherwise the reason it was rejected.</summary>
    private static string? Validate(string value) =>
        Pattern.IsMatch(value) ? null : $"Branch must match rix/* pattern, got: {value}";
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
