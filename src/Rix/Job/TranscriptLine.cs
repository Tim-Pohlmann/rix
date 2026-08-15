using System.Text.Json;

namespace Rix.Job;

/// <summary>
/// Rendering helpers shared by the agent transcript parsers (line-guarding itself is
/// <see cref="JsonLine.Read{T}"/>).
/// </summary>
internal static class TranscriptLine
{
    /// <summary>
    /// The longest a tool call's arguments may render as on one transcript line. Anything longer
    /// (e.g. an edit's old/new content) is truncated, so one oversized argument can't bloat the
    /// transcript and push the job summary toward GitHub's step-summary size cap.
    /// </summary>
    internal const int MaxToolInputLength = 400;

    /// <summary>
    /// Formats a tool call as the compact one-liner used across every agent's transcript. When the
    /// call carries <paramref name="input"/>, it is appended as compact JSON — a transcript that
    /// only names the tool (<c>→ read(...)</c>) doesn't show what the agent actually did, whereas
    /// <c>→ read({"file_path":"src/Program.cs"})</c> does.
    /// </summary>
    internal static string FormatToolCall(string name, JsonElement? input = null)
    {
        if (input is JsonElement element && element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Any())
        {
            var args = JsonSerializer.Serialize(element);
            if (args.Length > MaxToolInputLength)
                args = args[..MaxToolInputLength] + "…";
            return $"→ {name}({args})";
        }
        return $"→ {name}(...)";
    }

    /// <summary>
    /// Renders a single Anthropic-style content block — <c>{"type":"text","text":...}</c> verbatim,
    /// <c>{"type":toolUseType,"name":...}</c> as a compact one-liner — or <c>null</c> for anything
    /// else (e.g. tool-result feedback). Shared by the two agents (Claude, Pi) whose transcripts are
    /// arrays of these blocks; <paramref name="toolUseType"/> is the one detail that differs between
    /// them (<c>"tool_use"</c> vs. <c>"toolCall"</c>), as is the property carrying the tool call's
    /// arguments (<c>"input"</c> vs. <c>"arguments"</c>).
    /// </summary>
    internal static string? RenderContentBlock(JsonElement block, string toolUseType, string inputProperty)
    {
        if (!block.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            return null;
        var blockType = type.GetString();
        if (blockType == "text" && block.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString();
        if (blockType == toolUseType && block.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            block.TryGetProperty(inputProperty, out var input);
            return FormatToolCall(name.GetString()!, input);
        }
        return null;
    }

    /// <summary>
    /// Renders every block in a content array via <see cref="RenderContentBlock"/> and joins the
    /// non-null results with newlines, or returns <c>null</c> when nothing rendered.
    /// </summary>
    internal static string? JoinContentBlocks(JsonElement content, string toolUseType, string inputProperty)
    {
        var rendered = content.EnumerateArray().Select(block => RenderContentBlock(block, toolUseType, inputProperty));
        return JoinNonNull(rendered);
    }

    /// <summary>Joins the non-null items with newlines, or returns <c>null</c> when none are.</summary>
    internal static string? JoinNonNull(IEnumerable<string?> items)
    {
        var present = items.Where(item => item is not null).ToList();
        if (present.Count == 0)
            return null;
        return string.Join("\n", present);
    }
}
