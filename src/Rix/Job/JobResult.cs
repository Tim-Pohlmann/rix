using System.Text.Json.Serialization;

namespace Rix.Job;

[JsonDerivedType(typeof(JobSuccess), "success")]
[JsonDerivedType(typeof(JobFailure), "failure")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
internal interface IJobResult
{
    int TokensUsed { get; }
    TimeSpan Duration { get; }
    int DurationSeconds { get; }
}

internal record JobSuccess(
    [property: JsonPropertyName("prs")] IReadOnlyList<PullRequest> Prs,
    [property: JsonPropertyName("tokensUsed")] int TokensUsed,
    [property: JsonIgnore] TimeSpan Duration
) : IJobResult
{
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds => (int)Duration.TotalSeconds;
}

internal record JobFailure(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("tokensUsed")] int TokensUsed,
    [property: JsonIgnore] TimeSpan Duration
) : IJobResult
{
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds => (int)Duration.TotalSeconds;
}
