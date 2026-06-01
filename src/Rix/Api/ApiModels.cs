using System.Text.Json.Serialization;

namespace Rix.Api;

internal record PrRequest(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("baseBranch")] string BaseBranch
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
internal partial class ApiJsonContext : JsonSerializerContext { }
