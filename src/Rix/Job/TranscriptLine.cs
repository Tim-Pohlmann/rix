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

    /// <summary>
    /// Renders a single Anthropic-style content block — <c>{"type":"text","text":...}</c> verbatim,
    /// <c>{"type":toolUseType,"name":...}</c> as a compact one-liner — or <c>null</c> for anything
    /// else (e.g. tool-result feedback). Shared by the two agents (Claude, Pi) whose transcripts are
    /// arrays of these blocks; <paramref name="toolUseType"/> is the one detail that differs between
    /// them (<c>"tool_use"</c> vs. <c>"toolCall"</c>).
    /// </summary>
    internal static string? RenderContentBlock(JsonElement block, string toolUseType)
    {
        if (!block.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            return null;
        var blockType = type.GetString();
        if (blockType == "text" && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString();
        if (blockType == toolUseType && block.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            return $"→ {name.GetString()}(...)";
        return null;
    }

    /// <summary>
    /// Renders every block in a content array via <see cref="RenderContentBlock"/> and joins the
    /// non-null results with newlines, or returns <c>null</c> when nothing rendered.
    /// </summary>
    internal static string? JoinContentBlocks(JsonElement content, string toolUseType)
    {
        var blocks = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            if (RenderContentBlock(block, toolUseType) is { } rendered)
                blocks.Add(rendered);
        }
        if (blocks.Count == 0)
            return null;
        return string.Join("\n", blocks);
    }
}
