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

internal abstract record JobResultBase
(
    [property: JsonPropertyName("costUsd")] decimal CostUsd,
    [property: JsonIgnore] TimeSpan Duration
) : IJobResult
{
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds => (int)Duration.TotalSeconds;
}

internal record JobSuccess
(
    [property: JsonPropertyName("pendingPrRequests")] IReadOnlyList<PendingPr> PendingPrRequests,
    [property: JsonPropertyName("pendingPushRequests")] IReadOnlyList<PendingPush> PendingPushRequests,
    [property: JsonPropertyName("pendingUpdateRequests")] IReadOnlyList<PendingTaskUpdate> PendingUpdateRequests,
    [property: JsonPropertyName("pendingRevertRequests")] IReadOnlyList<PendingTaskRevert> PendingRevertRequests,
    decimal CostUsd,
    TimeSpan Duration
) : JobResultBase(CostUsd, Duration);

/// <summary>Shared base for the two failure shapes — a job that ran and failed
/// (<see cref="JobFailure"/>) and a failure before the job could run (<see cref="SetupFailure"/>).
/// They are <em>siblings</em> rather than one deriving from the other, so the exit-code switch in
/// <see cref="Rix.Startup"/> matches disjoint concrete types and is no longer order-sensitive.</summary>
internal abstract record JobFailureBase
(
    [property: JsonPropertyName("error")] string Error,
    decimal CostUsd,
    TimeSpan Duration
) : JobResultBase(CostUsd, Duration);

internal sealed record JobFailure
(
    string Error,
    decimal CostUsd,
    TimeSpan Duration
) : JobFailureBase(Error, CostUsd, Duration);

/// <summary>
/// A failure before the job proper could run (e.g. agent install failed). Serializes as
/// <c>"status": "setupFailure"</c> so the shell can map it to a distinct exit code; carries no cost
/// or duration. A sibling of <see cref="JobFailure"/>, not a specialization of it.
/// </summary>
internal sealed record SetupFailure(string Error)
    : JobFailureBase(Error, CostUsd: 0m, Duration: TimeSpan.Zero);
