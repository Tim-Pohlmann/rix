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
    private static readonly Regex Pattern =
        new(@"^rix/.+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    internal RixBranchName(string value) : base(value)
    {
        if (!Pattern.IsMatch(value))
            throw new ArgumentException($"Branch must match rix/* pattern, got: {value}", nameof(value));
    }
}

internal sealed class BranchNameJsonConverter : StringValueJsonConverter<BranchName>
{
    protected override BranchName Create(string value) => new(value);
    protected override string Extract(BranchName value) => value.Value;
}

internal sealed class RixBranchNameJsonConverter : StringValueJsonConverter<RixBranchName>
{
    protected override RixBranchName Create(string value) => new(value);
    protected override string Extract(RixBranchName value) => value.Value;
}
