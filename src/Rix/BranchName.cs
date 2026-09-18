using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rix;

[JsonConverter(typeof(BranchNameJsonConverter))]
internal record BranchName(string Value)
{
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
