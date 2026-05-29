using System.Text.Json.Serialization;

namespace Rix.Job;

internal record PrInfo(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("branch")] string Branch
);

internal abstract record JobOutcome(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("tokensUsed")] int TokensUsed,
    [property: JsonPropertyName("durationSeconds")] int DurationSeconds
);

internal record JobSuccess(
    [property: JsonPropertyName("prs")] IReadOnlyList<PrInfo> Prs,
    int TokensUsed,
    int DurationSeconds
) : JobOutcome("success", TokensUsed, DurationSeconds);

internal record JobFailure(
    [property: JsonPropertyName("error")] string Error,
    int TokensUsed,
    int DurationSeconds
) : JobOutcome("failure", TokensUsed, DurationSeconds);
