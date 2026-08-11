using System.Text.Json.Serialization;

namespace Rix.Api;

internal record PrRequest
(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("baseBranch")] string BaseBranch
);

/// <summary>The body of a POST to <c>/push</c>: the branch the agent wants to push its new commits
/// to, and the branch the commits are based on (used to bundle exactly the new commits). No
/// title/body — unlike <c>/pr</c> this delivers to an existing branch rather than opening a PR.</summary>
internal record PushRequest
(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("baseBranch")] string BaseBranch
);

internal record QueuedResponse([property: JsonPropertyName("status")] string Status);

/// <summary>The body of a DELETE to <c>/pr</c> or <c>/push</c>: the branch whose queued request the
/// agent wants to cancel. Branches are the queue's key, so at most one request per branch is ever
/// queued and removing it clears that branch's slot entirely.</summary>
internal record DeleteRequest([property: JsonPropertyName("branch")] string Branch);

internal record ErrorResponse([property: JsonPropertyName("error")] string Error);

[JsonSerializable(typeof(PrRequest))]
[JsonSerializable(typeof(PushRequest))]
[JsonSerializable(typeof(DeleteRequest))]
[JsonSerializable(typeof(QueuedResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(IReadOnlyList<QueuedPr>))]
[JsonSerializable(typeof(IReadOnlyList<QueuedPush>))]
internal partial class ApiJsonContext : JsonSerializerContext { }
