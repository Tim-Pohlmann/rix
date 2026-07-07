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
            "--output-format", "stream-json", "--print", config.Agent.Prompt,
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
    public decimal? ParseCost(string outputLine) => CostLine.Read(outputLine, "\"total_cost_usd\"", ReadCost);

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
}
