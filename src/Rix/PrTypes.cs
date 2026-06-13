using System.Text.Json.Serialization;

namespace Rix;

[JsonConverter(typeof(PrTitleJsonConverter))]
internal readonly record struct PrTitle(string Value);

[JsonConverter(typeof(PrBodyJsonConverter))]
internal readonly record struct PrBody(string Value);

internal sealed class PrTitleJsonConverter : StringValueJsonConverter<PrTitle>
{
    protected override PrTitle Create(string value) => new(value);
    protected override string Extract(PrTitle value) => value.Value;
}

internal sealed class PrBodyJsonConverter : StringValueJsonConverter<PrBody>
{
    protected override PrBody Create(string value) => new(value);
    protected override string Extract(PrBody value) => value.Value;
}

internal record QueuedPr(RixBranchName Branch, BranchName BaseBranch, PrTitle Title, PrBody Body);

internal record PendingPr(
    [property: JsonPropertyName("branch")] RixBranchName Branch,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch,
    [property: JsonPropertyName("title")] PrTitle Title,
    [property: JsonPropertyName("body")] PrBody Body,
    [property: JsonPropertyName("bundleFile")] string BundleFile
);
