using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rix;

[JsonConverter(typeof(BranchNameJsonConverter))]
internal record BranchName(string Value)
{
    /// <summary>Parses raw text into a <see cref="BranchName"/>. Currently every non-null string is
    /// accepted, but the boundary exists so future format rules can return a
    /// <see cref="ParseError{T}"/> alongside the other value objects rather than throwing.</summary>
    internal static ParseResult<BranchName> Parse(string value) => new ParseSuccess<BranchName>(new BranchName(value));

    /// <summary>Parses a raw comma-separated branch list — the form <c>--allowed-push-branches</c>
    /// arrives in — into the branches a push may deliver to. Blank input (the flag was never set)
    /// means nothing is permitted, so the result is the empty list: an operator must opt in to
    /// pushing onto an existing branch at all. Unlike the <c>rix/*</c>-restricted branches the agent
    /// creates via <c>/pr</c>, any branch name is acceptable here, since these already exist on the
    /// remote before the job ever runs. Duplicates are dropped.
    ///
    /// Shared by <c>rix job</c> and <c>rix submit</c> so the same text can't mean two different
    /// lists at the two points that enforce it. The comma-separated form cannot express a branch
    /// whose name contains a comma; a caller holding real <see cref="BranchName"/>s passes them
    /// directly instead (see <c>CiFailureConfig.ToJobConfig</c>).</summary>
    internal static List<BranchName> ParseAllowList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry => new BranchName(entry))
            .Distinct()
            .ToList();
    }

    public override string ToString() => Value;
}

[JsonConverter(typeof(RixBranchNameJsonConverter))]
internal record RixBranchName : BranchName
{
    private static readonly Regex Pattern = new(@"^rix/.+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    internal RixBranchName(string value) : base(value)
    {
        if (Validate(value) is { } error)
            throw new ArgumentException(error, nameof(value));
    }

    /// <summary>Returns the union path's <see cref="RixBranchName"/> or, for malformed input, a
    /// <see cref="ParseError{T}"/> callers can aggregate instead of catching an exception.</summary>
    internal static new ParseResult<RixBranchName> Parse(string value)
    => Validate(value) switch
    {
        { } error => new ParseError<RixBranchName>(error),
        _ => new ParseSuccess<RixBranchName>(new RixBranchName(value))
    };

    /// <summary>The single source of the <c>rix/*</c> rule and its message, shared by the throwing
    /// constructor and the non-throwing <see cref="Parse"/>: null when <paramref name="value"/> is
    /// valid, otherwise the reason it was rejected.</summary>
    private static string? Validate(string value)
    => Pattern.IsMatch(value) switch { true => null, false => $"Branch must match rix/* pattern, got: {value}" };
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
