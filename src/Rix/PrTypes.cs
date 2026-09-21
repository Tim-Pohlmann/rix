using System.Text.Json.Serialization;

namespace Rix;

[JsonConverter(typeof(PrTitleJsonConverter))]
internal readonly record struct PrTitle(string Value);

[JsonConverter(typeof(PrBodyJsonConverter))]
internal readonly record struct PrBody(string Value);

internal sealed class PrTitleJsonConverter()
    : StringValueJsonConverter<PrTitle>(value => new PrTitle(value), title => title.Value);

internal sealed class PrBodyJsonConverter()
    : StringValueJsonConverter<PrBody>(value => new PrBody(value), body => body.Value);

internal record QueuedPr
(
    [property: JsonPropertyName("branch")] RixBranchName Branch,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch,
    [property: JsonPropertyName("title")] PrTitle Title,
    [property: JsonPropertyName("body")] PrBody Body
);

internal record PendingPr
(
    [property: JsonPropertyName("branch")] RixBranchName Branch,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch,
    [property: JsonPropertyName("title")] PrTitle Title,
    [property: JsonPropertyName("body")] PrBody Body,
    [property: JsonPropertyName("bundleFile")] string BundleFile
);

/// <summary>A request to push the agent's new commits onto a branch that already exists on the
/// remote (e.g. continuing work from a previous run, including a human's own branch). Unlike a
/// <see cref="QueuedPr"/>, no PR is opened — the commits are delivered straight to the existing
/// branch, so its name is never one the agent is inventing and the <c>rix/*</c> pattern doesn't
/// apply here.</summary>
internal record QueuedPush
(
    [property: JsonPropertyName("branch")] BranchName Branch,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch
);

internal record PendingPush
(
    [property: JsonPropertyName("branch")] BranchName Branch,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch,
    [property: JsonPropertyName("bundleFile")] string BundleFile
);
