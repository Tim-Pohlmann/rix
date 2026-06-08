using System.Text.Json;

namespace Rix.Job;

/// <summary>
/// Pure parsing of Claude's NDJSON output lines into accumulated token usage.
/// No I/O and no mutable state — given a running total and a line it returns the new total.
/// </summary>
internal static class TokenUsage
{
    /// <summary>
    /// Returns <paramref name="current"/> plus any token counts found in <paramref name="line"/>
    /// (clamped to <see cref="int.MaxValue"/>). Lines that are not a Claude <c>result</c> JSON
    /// object are ignored and the running total is returned unchanged.
    /// </summary>
    internal static int Accumulate(int current, string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return current;
        if (!trimmed.Contains("\"total_input_tokens\"") && !trimmed.Contains("\"total_output_tokens\"")) return current;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String || type.GetString() != "result")
                return current;
            var input = ReadTokenCount(root, "total_input_tokens");
            var output = ReadTokenCount(root, "total_output_tokens");
            return (int)Math.Min((long)current + input + output, int.MaxValue);
        }
        catch (JsonException) { /* malformed JSON line — skip */ }
        return current;
    }

    private static long ReadTokenCount(JsonElement root, string property) =>
        root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v) ? v : 0L;
}
