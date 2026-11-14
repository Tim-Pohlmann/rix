using System.Text.Json.Serialization;

namespace Rix.Agent;

[JsonDerivedType(typeof(AgentSuccess), "success")]
[JsonDerivedType(typeof(AgentFailure), "failure")]
[JsonDerivedType(typeof(SetupFailure), "setupFailure")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
internal interface IAgentResult
{
    decimal CostUsd { get; }
    TimeSpan Duration { get; }
    int DurationSeconds { get; }
}

internal abstract record AgentResultBase
(
    [property: JsonPropertyName("costUsd")] decimal CostUsd,
    [property: JsonIgnore] TimeSpan Duration
) : IAgentResult
{
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds => (int)Duration.TotalSeconds;
}

internal sealed record AgentSuccess
(
    [property: JsonPropertyName("pendingPrRequests")] IReadOnlyList<PendingPr> PendingPrRequests,
    [property: JsonPropertyName("pendingPushRequests")] IReadOnlyList<PendingPush> PendingPushRequests,
    decimal CostUsd,
    TimeSpan Duration
) : AgentResultBase(CostUsd, Duration);

/// <summary>Shared base for the two failure shapes — an agent that ran and failed
/// (<see cref="AgentFailure"/>) and a failure before the agent could run (<see cref="SetupFailure"/>).
/// They are <em>siblings</em> rather than one deriving from the other, so the exit-code switch in
/// <see cref="Rix.Cli.AgentCommand"/> matches disjoint concrete types and is no longer
/// order-sensitive.</summary>
internal abstract record AgentFailureBase
(
    [property: JsonPropertyName("error")] string Error,
    decimal CostUsd,
    TimeSpan Duration
) : AgentResultBase(CostUsd, Duration);

internal sealed record AgentFailure
(
    string Error,
    decimal CostUsd,
    TimeSpan Duration
) : AgentFailureBase(Error, CostUsd, Duration);

/// <summary>
/// A failure before the agent could run (e.g. agent install failed). Serializes as
/// <c>"status": "setupFailure"</c> so the shell can map it to a distinct exit code; carries no cost
/// or duration. A sibling of <see cref="AgentFailure"/>, not a specialization of it.
/// </summary>
internal sealed record SetupFailure(string Error)
    : AgentFailureBase(Error, CostUsd: 0m, Duration: TimeSpan.Zero);
