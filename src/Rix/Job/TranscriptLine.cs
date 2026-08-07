using System.Text.Json;

namespace Rix.Job;

/// <summary>
/// Shared scaffold for the agent transcript parsers. Guards a single stdout line, parses it as a
/// JSON object, and hands the root element to <paramref name="readTranscript"/>. Returns
/// <c>null</c> for blank lines, non-object or malformed JSON, or lines that don't contain
/// <paramref name="marker"/> — so each agent's parser only has to express how to read the
/// human-readable content, not how to validate the line.
/// </summary>
internal static class TranscriptLine
{
    internal static string? Read(string line, string marker, Func<JsonElement, string?> readTranscript)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return null;
        if (!trimmed.Contains(marker)) return null;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return readTranscript(doc.RootElement);
            return null;
        }
        catch (JsonException) { /* malformed JSON line — skip */ }
        return null;
    }
}
