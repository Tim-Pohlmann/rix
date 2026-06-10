using System.Text.Json.Serialization;

namespace Rix.Job;

[JsonDerivedType(typeof(JobSuccess), "success")]
[JsonDerivedType(typeof(JobFailure), "failure")]
[JsonDerivedType(typeof(SetupFailure), "setupFailure")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
internal interface IJobResult
{
    decimal CostUsd { get; }
    TimeSpan Duration { get; }
    int DurationSeconds { get; }
}

internal abstract record JobResultBase(
    [property: JsonPropertyName("costUsd")] decimal CostUsd,
    [property: JsonIgnore] TimeSpan Duration
) : IJobResult
{
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds => (int)Duration.TotalSeconds;
}

internal record JobSuccess(
    [property: JsonPropertyName("pendingPrRequests")] IReadOnlyList<PendingPr> PendingPrRequests,
    decimal CostUsd,
    TimeSpan Duration
) : JobResultBase(CostUsd, Duration);

internal record JobFailure(
    [property: JsonPropertyName("error")] string Error,
    decimal CostUsd,
    TimeSpan Duration
) : JobResultBase(CostUsd, Duration);

/// <summary>
/// A failure before the job proper could run (e.g. Claude install failed). Serializes as
/// <c>"status": "setupFailure"</c> so the shell can map it to a distinct exit code, while still
/// being a <see cref="JobFailure"/> with no cost or duration.
/// </summary>
internal sealed record SetupFailure(string Error)
    : JobFailure(Error, CostUsd: 0m, Duration: TimeSpan.Zero);
