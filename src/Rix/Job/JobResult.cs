using System.Text.Json.Serialization;

namespace Rix.Job;

internal record PrInfo(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("branch")] string Branch
);

[JsonDerivedType(typeof(JobSuccess), "success")]
[JsonDerivedType(typeof(JobFailure), "failure")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
internal interface IJobOutcome
{
    int TokensUsed { get; }
    int DurationSeconds { get; }
}

internal record JobSuccess(
    [property: JsonPropertyName("prs")] IReadOnlyList<PrInfo> Prs,
    [property: JsonPropertyName("tokensUsed")] int TokensUsed,
    [property: JsonPropertyName("durationSeconds")] int DurationSeconds
) : IJobOutcome;

internal record JobFailure(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("tokensUsed")] int TokensUsed,
    [property: JsonPropertyName("durationSeconds")] int DurationSeconds
) : IJobOutcome;
