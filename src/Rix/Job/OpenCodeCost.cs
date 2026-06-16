using System.Text.Json;

namespace Rix.Job;

/// <summary>
/// Pure parsing of OpenCode's <c>--format json</c> output into the run's USD cost. OpenCode
/// streams one JSON event per line; the cost (when known) rides on a <c>step_finish</c> event
/// inside its <c>part.cost</c> field. Unlike Claude, OpenCode does not emit a single authoritative
/// cumulative total — and it frequently reports <c>0</c> because costs are derived from external
/// (LiteLLM) pricing that may be unavailable — so callers should treat a missing cost as unknown.
/// Lines that are not a cost-bearing event yield <c>null</c>.
/// </summary>
internal static class OpenCodeCost
{
    /// <summary>
    /// Returns the USD cost carried by an OpenCode JSON event line, or <c>null</c> when the line
    /// is not valid JSON or carries no numeric cost. The cost is read from <c>part.cost</c> (a
    /// <c>step_finish</c> event) and, as a fallback, a top-level <c>cost</c> field.
    /// </summary>
    internal static decimal? FromEventLine(string line) =>
        CostLine.Read(line, "\"cost\"", root =>
            root.TryGetProperty("part", out var part) &&
            part.ValueKind == JsonValueKind.Object &&
            TryReadCost(part, out var partCost)
                ? partCost
                : TryReadCost(root, out var rootCost) ? rootCost : null);

    private static bool TryReadCost(JsonElement element, out decimal cost)
    {
        if (element.TryGetProperty("cost", out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out cost))
            return true;

        cost = 0m;
        return false;
    }
}
