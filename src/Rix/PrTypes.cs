using System.Text.Json.Serialization;

namespace Rix;

[JsonConverter(typeof(PrTitleJsonConverter))]
internal readonly record struct PrTitle(string Value)
{
    /// <summary>Parses raw text into a <see cref="PrTitle"/>. Currently always succeeds; the
    /// boundary exists so future rules (e.g. length limits) can return a <see cref="ParseError{T}"/>
    /// alongside the other value objects rather than throwing.</summary>
    internal static ParseResult<PrTitle> Parse(string value) => new ParseSuccess<PrTitle>(new PrTitle(value));
}

[JsonConverter(typeof(PrBodyJsonConverter))]
internal readonly record struct PrBody(string Value)
{
    /// <summary>Parses raw text into a <see cref="PrBody"/>. Currently always succeeds; the boundary
    /// exists so future rules can return a <see cref="ParseError{T}"/> rather than throwing.</summary>
    internal static ParseResult<PrBody> Parse(string value) => new ParseSuccess<PrBody>(new PrBody(value));
}

internal sealed class PrTitleJsonConverter : StringValueJsonConverter<PrTitle>
{
    protected override ParseResult<PrTitle> Parse(string value) => PrTitle.Parse(value);
    protected override string Extract(PrTitle value) => value.Value;
}

internal sealed class PrBodyJsonConverter : StringValueJsonConverter<PrBody>
{
    protected override ParseResult<PrBody> Parse(string value) => PrBody.Parse(value);
    protected override string Extract(PrBody value) => value.Value;
}

internal record QueuedPr(RixBranchName Branch, BranchName BaseBranch, PrTitle Title, PrBody Body);

internal record PendingPr
(
    [property: JsonPropertyName("branch")] RixBranchName Branch,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch,
    [property: JsonPropertyName("title")] PrTitle Title,
    [property: JsonPropertyName("body")] PrBody Body,
    [property: JsonPropertyName("bundleFile")] string BundleFile
);

/// <summary>A request to push the agent's new commits onto a branch that already exists on the
/// remote (e.g. continuing work from a previous run). Unlike a <see cref="QueuedPr"/>, no PR is
/// opened — the commits are delivered straight to the existing branch.</summary>
internal record QueuedPush(RixBranchName Branch, BranchName BaseBranch);

internal record PendingPush
(
    [property: JsonPropertyName("branch")] RixBranchName Branch,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch,
    [property: JsonPropertyName("bundleFile")] string BundleFile
);
