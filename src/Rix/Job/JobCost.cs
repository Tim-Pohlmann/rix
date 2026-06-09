using System.Text.Json;

namespace Rix.Job;

/// <summary>
/// Pure parsing of Claude's NDJSON output into the run's USD cost. Claude emits a single
/// terminal <c>result</c> line whose <c>total_cost_usd</c> is the cumulative cost for the run;
/// non-result lines (and results without a cost) yield <c>null</c> so the caller keeps the
/// last known value.
/// </summary>
internal static class JobCost
{
    /// <summary>
    /// Returns the <c>total_cost_usd</c> from a Claude <c>result</c> line, or <c>null</c> when
    /// <paramref name="line"/> is not a result object or carries no numeric cost.
    /// </summary>
    internal static decimal? FromResultLine(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return null;
        if (!trimmed.Contains("\"total_cost_usd\"")) return null;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String || type.GetString() != "result")
                return null;
            return root.TryGetProperty("total_cost_usd", out var cost) &&
                   cost.ValueKind == JsonValueKind.Number && cost.TryGetDecimal(out var v)
                ? v : null;
        }
        catch (JsonException) { /* malformed JSON line — skip */ }
        return null;
    }
}
