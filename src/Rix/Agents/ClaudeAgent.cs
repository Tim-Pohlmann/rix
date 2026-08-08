using Rix.Job;
using Rix.Process;
using System.Text.Json;

namespace Rix.Agents;

/// <summary>
/// <see cref="ICodingAgent"/> backed by Anthropic's Claude Code CLI: installs it via npm,
/// launches it in streaming-JSON print mode, and reads cost from its NDJSON output.
/// </summary>
internal sealed class ClaudeAgent : ICodingAgent
{
    private const string Package = "@anthropic-ai/claude-code";

    public Task<InstallResult> EnsureInstalledAsync(RunProcessAsync runProcess, CancellationToken cancellationToken)
    => CodingAgentHelper.EnsureInstalledViaNpmAsync(runProcess, "claude", Package, cancellationToken);

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt)
    {
        List<string> args =
        [
            // --verbose is required by the CLI whenever --print is combined with
            // --output-format=stream-json; omitting it fails fast with a usage error.
            "--output-format", "stream-json", "--print", "--verbose", config.Agent.Prompt,
            "--append-system-prompt", systemPrompt,
        ];
        if (!string.IsNullOrWhiteSpace(config.Agent.Model))
            args.AddRange(["--model", config.Agent.Model]);

        return new
        (
            FileName: "claude",
            Arguments: args,
            EnvironmentOverrides: new Dictionary<string, string>
            {
                ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.Agent.MaxTokens.Value.ToString(),
            }
        );
    }

    /// <summary>
    /// Reads cost from Claude's NDJSON output. Claude emits a single terminal <c>result</c> line
    /// whose <c>total_cost_usd</c> is the run's cumulative cost; other lines (and results without a
    /// cost) yield <c>null</c> so the caller keeps the last known value.
    /// </summary>
    public decimal? ParseCost(string outputLine) => JsonLine.Read(outputLine, "\"total_cost_usd\"", ReadCost);

    /// <summary>
    /// Reads transcript content from Claude's NDJSON output: each <c>assistant</c> envelope's
    /// <c>message.content</c> blocks, rendered as text verbatim and tool calls as a compact
    /// one-liner. System, user (tool-result feedback — can be large/binary) and result lines
    /// (already consumed by <see cref="ParseCost"/>) yield <c>null</c>.
    /// </summary>
    public string? ParseTranscriptLine(string outputLine) => JsonLine.Read(outputLine, "\"type\":\"assistant\"", ReadTranscript);

    private static decimal? ReadCost(JsonElement root)
    {
        if
        (
            root.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String && type.GetString() == "result" &&
            root.TryGetProperty("total_cost_usd", out var cost) &&
            cost.ValueKind == JsonValueKind.Number && cost.TryGetDecimal(out var v)
        )
            return v;

        return null;
    }

    private static string? ReadTranscript(JsonElement root)
    {
        if
        (
            root.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String && type.GetString() == "assistant" &&
            root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
        )
            return TranscriptLine.JoinContentBlocks(content, "tool_use");

        return null;
    }
}
