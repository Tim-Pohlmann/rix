using System.Text.Json.Serialization;

namespace Rix.Api;

internal record PrRequest(
    [property: JsonPropertyName("branch")] RixBranchName Branch,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch
);

internal record PrQueuedResponse(
    [property: JsonPropertyName("status")] string Status
);

internal record ErrorResponse(
    [property: JsonPropertyName("error")] string Error
);

[JsonSerializable(typeof(PrRequest))]
[JsonSerializable(typeof(PrQueuedResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(RixBranchName))]
[JsonSerializable(typeof(BranchName))]
internal partial class ApiJsonContext : JsonSerializerContext { }
