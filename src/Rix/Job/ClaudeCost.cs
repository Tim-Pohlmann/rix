using System.Text.Json;

namespace Rix.Job;

/// <summary>
/// Pure parsing of Claude's NDJSON output into the run's USD cost. Claude emits a single
/// terminal <c>result</c> line whose <c>total_cost_usd</c> is the cumulative cost for the run;
/// non-result lines (and results without a cost) yield <c>null</c> so the caller keeps the
/// last known value.
/// </summary>
internal static class ClaudeCost
{
    /// <summary>
    /// Returns the <c>total_cost_usd</c> from a Claude <c>result</c> line, or <c>null</c> when
    /// <paramref name="line"/> is not a result object or carries no numeric cost.
    /// </summary>
    internal static decimal? FromResultLine(string line) =>
        CostLine.Read(line, "\"total_cost_usd\"", root =>
            root.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String && type.GetString() == "result" &&
            root.TryGetProperty("total_cost_usd", out var cost) &&
            cost.ValueKind == JsonValueKind.Number && cost.TryGetDecimal(out var v)
                ? v : null);
}
