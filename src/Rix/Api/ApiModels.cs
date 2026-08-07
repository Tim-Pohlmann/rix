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

/// <summary>The body of a POST to <c>/tasks/update</c>: the branch whose open pull request to
/// update, plus the new <c>title</c> and/or <c>body</c>. At least one of the two must be supplied;
/// omitted ones keep their current value on the remote.</summary>
internal record UpdateTaskRequest
(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("body")] string? Body
);

/// <summary>The body of a POST to <c>/tasks/revert</c>: the branch whose open pull request to
/// close.</summary>
internal record RevertTaskRequest
(
    [property: JsonPropertyName("branch")] string Branch
);

internal record QueuedResponse([property: JsonPropertyName("status")] string Status);

internal record ErrorResponse([property: JsonPropertyName("error")] string Error);

[JsonSerializable(typeof(PrRequest))]
[JsonSerializable(typeof(PushRequest))]
[JsonSerializable(typeof(UpdateTaskRequest))]
[JsonSerializable(typeof(RevertTaskRequest))]
[JsonSerializable(typeof(RemotePr))]
[JsonSerializable(typeof(QueuedResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class ApiJsonContext : JsonSerializerContext { }
