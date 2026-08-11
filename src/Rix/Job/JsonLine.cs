using System.Text.Json;

namespace Rix.Job;

/// <summary>
/// Shared scaffold for the agent output-line parsers (cost, transcript). Guards a single stdout
/// line, parses it as a JSON object, and hands the root element to <paramref name="read"/>.
/// Returns <c>default</c> for blank lines, non-object or malformed JSON, or lines that don't
/// contain <paramref name="marker"/> — so each parser only has to express how to read its value,
/// not how to validate the line.
/// </summary>
internal static class JsonLine
{
    internal static T? Read<T>(string line, string marker, Func<JsonElement, T?> read)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return default;
        if (!trimmed.Contains(marker)) return default;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return read(doc.RootElement);
            return default;
        }
        catch (JsonException) { /* malformed JSON line — skip */ }
        return default;
    }
}
