using System.Text.Json.Serialization;

namespace Rix.Api;

internal record PrRequest(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body
);

internal record PrCreatedResponse(
    [property: JsonPropertyName("url")] string Url
);

internal record ErrorResponse(
    [property: JsonPropertyName("error")] string Error
);

internal static class PrRequestExtensions
{
    internal static string? Validate(this PrRequest req)
    {
        if (!BranchName.IsValid(req.Branch))
            return $"Branch must match rix/* pattern, got: {req.Branch}";
        if (string.IsNullOrWhiteSpace(req.Title))
            return "title is required";
        return null;
    }
}

[JsonSerializable(typeof(PrRequest))]
[JsonSerializable(typeof(PrCreatedResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal partial class ApiJsonContext : JsonSerializerContext { }
