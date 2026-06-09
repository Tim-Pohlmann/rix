using System.Text.Json.Serialization;

namespace Rix.Job;

[JsonDerivedType(typeof(JobSuccess), "success")]
[JsonDerivedType(typeof(JobFailure), "failure")]
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
/// The outcome of a job run: the result to report plus the process exit code.
/// The exit code is carried explicitly because a setup failure and a job failure
/// share the same <see cref="JobFailure"/> JSON shape but map to different codes.
/// </summary>
internal sealed record JobOutcome(IJobResult Result, int ExitCode);
