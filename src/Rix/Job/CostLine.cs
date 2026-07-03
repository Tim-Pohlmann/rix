using System.Text.Json;

namespace Rix.Job;

/// <summary>
/// Shared scaffold for the agent cost parsers. Guards a single stdout line, parses it as a JSON
/// object, and hands the root element to <paramref name="readCost"/>. Returns <c>null</c> for blank
/// lines, non-object or malformed JSON, or lines that don't contain <paramref name="marker"/> — so
/// each agent's parser only has to express how to read the cost, not how to validate the line.
/// </summary>
internal static class CostLine
{
    internal static decimal? Read(string line, string marker, Func<JsonElement, decimal?> readCost)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return null;
        if (!trimmed.Contains(marker)) return null;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return readCost(doc.RootElement);
            return null;
        }
        catch (JsonException) { /* malformed JSON line — skip */ }
        return null;
    }
}
