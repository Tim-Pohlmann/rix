using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rix;

[JsonConverter(typeof(BranchNameJsonConverter))]
internal record BranchName(string Value)
{
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
        if (!Pattern.IsMatch(value))
            throw new InvalidInputException($"Branch must match rix/* pattern, got: {value}");
    }
}

internal sealed class BranchNameJsonConverter()
    : StringValueJsonConverter<BranchName>(value => new BranchName(value), branch => branch.Value);

internal sealed class RixBranchNameJsonConverter()
    : StringValueJsonConverter<RixBranchName>(value => new RixBranchName(value), branch => branch.Value);
