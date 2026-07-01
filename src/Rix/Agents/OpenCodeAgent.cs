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
/// Two deliberate differences from <see cref="ClaudeAgent"/>, both rooted in OpenCode's CLI:
/// <list type="bullet">
/// <item>OpenCode has no <c>--append-system-prompt</c> flag, so rix's system prompt (which carries
/// the local PR API URL) is folded into the run message ahead of the user's prompt.</item>
/// <item>OpenCode exposes no per-run output-token cap equivalent to
/// <c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c>, so <see cref="AgentConfig.MaxTokens"/> is not forwarded;
/// the invocation carries no environment overrides.</item>
/// </list>
/// </remarks>
internal sealed class OpenCodeAgent : ICodingAgent
{
    private const string Package = "opencode-ai";

    public Task<InstallResult> EnsureInstalledAsync(RunProcessAsync runProcess, CancellationToken cancellationToken)
    => CodingAgentHelper.EnsureInstalledViaNpmAsync(runProcess, "opencode", Package, cancellationToken);

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt)
    => new
    (
        FileName: "opencode",
        Arguments: ["run", $"{systemPrompt}\n\n{config.Agent.Prompt}", "--format", "json"],
        EnvironmentOverrides: new Dictionary<string, string>()
    );

    /// <summary>
    /// Reads cost from an OpenCode JSON event line. The cost (when known) rides on a
    /// <c>step_finish</c> event inside <c>part.cost</c>, with a top-level <c>cost</c> fallback.
    /// Unlike Claude, OpenCode emits no single authoritative total and frequently reports <c>0</c>
    /// (costs derive from external LiteLLM pricing that may be unavailable), so a missing cost is
    /// treated as unknown (<c>null</c>).
    /// </summary>
    public decimal? ParseCost(string outputLine) => CostLine.Read(outputLine, "\"cost\"", ReadCost);

    private static decimal? ReadCost(JsonElement root)
    {
        if (root.TryGetProperty("part", out var part) &&
            part.ValueKind == JsonValueKind.Object &&
            TryReadCost(part, out var partCost))
            return partCost;

        return TryReadCost(root, out var rootCost) ? rootCost : null;
    }

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
