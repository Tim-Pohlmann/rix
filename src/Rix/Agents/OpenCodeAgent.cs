using Rix.Job;
using Rix.Process;
using System.Text.Json;

namespace Rix.Agents;

/// <summary>
/// <see cref="ICodingAgent"/> backed by the open-source OpenCode CLI: installs it via npm,
/// launches it in non-interactive <c>run</c> mode with JSON event output, and reads cost from
/// that stream.
/// </summary>
/// <remarks>
/// Three deliberate differences from <see cref="ClaudeAgent"/>, all rooted in OpenCode's CLI:
/// <list type="bullet">
/// <item>OpenCode has no <c>--append-system-prompt</c> flag, so rix's system prompt (which carries
/// the local PR API URL) is folded into the run message ahead of the user's prompt.</item>
/// <item>OpenCode exposes no per-run output-token cap equivalent to
/// <c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c>, so <see cref="AgentConfig.MaxTokens"/> is not forwarded;
/// the invocation carries no environment overrides.</item>
/// <item>The invocation always carries <c>--auto</c>. Without it, an unattended run has no way to
/// answer an "ask"-level permission request (there's no terminal to prompt), so OpenCode
/// auto-rejects it — and, unlike Claude Code, OpenCode's session then stops outright rather than
/// letting the agent recover, silently abandoning the job while still exiting 0. <c>--auto</c>
/// resolves "ask" to allowed instead (explicit <c>deny</c> rules, of which rix configures none,
/// would still be enforced); acceptable here because the agent already runs confined to a
/// disposable <see cref="Rix.TempDirectory"/> clone on an ephemeral CI runner.</item>
/// </list>
/// Unlike Claude (Anthropic-only), OpenCode supports many model providers via
/// <see cref="AgentConfig.Model"/> (a <c>provider/model</c> string forwarded verbatim as
/// <c>--model</c>); the caller is responsible for exporting whatever credential env var that
/// provider expects, since rix inherits its process environment into the spawned CLI unchanged.
/// Local/self-hosted backends (Ollama, LM Studio) aren't covered by this — they need a generated
/// <c>opencode.json</c> config rather than a model string + API key, which is a future extension.
/// </remarks>
internal sealed class OpenCodeAgent : ICodingAgent
{
    private const string Package = "opencode-ai";

    public Task<InstallResult> EnsureInstalledAsync(RunProcessAsync runProcess, CancellationToken cancellationToken)
    => CodingAgentHelper.EnsureInstalledViaNpmAsync(runProcess, "opencode", Package, cancellationToken);

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt)
    {
        List<string> args = ["run", $"{systemPrompt}\n\n{config.Agent.Prompt}", "--auto"];
        if (!string.IsNullOrWhiteSpace(config.Agent.Model))
            args.AddRange(["--model", config.Agent.Model]);
        args.AddRange(["--format", "json"]);

        return new
        (
            FileName: "opencode",
            Arguments: args,
            EnvironmentOverrides: new Dictionary<string, string>()
        );
    }

    /// <summary>
    /// Reads cost from an OpenCode JSON event line. The cost (when known) rides on a
    /// <c>step_finish</c> event inside <c>part.cost</c>, with a top-level <c>cost</c> fallback.
    /// Unlike Claude, OpenCode emits no single authoritative total and frequently reports <c>0</c>
    /// (costs derive from external LiteLLM pricing that may be unavailable), so a missing cost is
    /// treated as unknown (<c>null</c>).
    /// </summary>
    public decimal? ParseCost(string outputLine) => JsonLine.Read(outputLine, "\"cost\"", ReadCost);

    /// <summary>
    /// Reads transcript content from an OpenCode JSON event line: <c>text</c> events' <c>part.text</c>
    /// verbatim, and <c>tool_use</c> events as a compact tool-name one-liner. Everything else
    /// (step bookkeeping, cost-bearing <c>step_finish</c> lines) yields <c>null</c>.
    /// </summary>
    public string? ParseTranscriptLine(string outputLine) => JsonLine.Read(outputLine, "\"part\"", ReadTranscript);

    private static decimal? ReadCost(JsonElement root)
    {
        if (root.TryGetProperty("part", out var part) && part.ValueKind == JsonValueKind.Object)
        {
            var partCost = ReadCostValue(part);
            if (partCost is not null)
                return partCost;
        }

        return ReadCostValue(root);
    }

    private static decimal? ReadCostValue(JsonElement element)
    {
        if
        (
            element.TryGetProperty("cost", out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDecimal(out var cost)
        )
            return cost;

        return null;
    }

    private static string? ReadTranscript(JsonElement root)
    {
        if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            return null;
        if (!root.TryGetProperty("part", out var part) || part.ValueKind != JsonValueKind.Object)
            return null;
        switch (type.GetString())
        {
            case "text" when part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String:
                return text.GetString();
            case "tool_use" when part.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.String:
                return TranscriptLine.FormatToolCall(tool.GetString()!);
            default:
                return null;
        }
    }
}
