using Rix.Job;
using Rix.Process;
using System.Text.Json;

namespace Rix.Agents;

/// <summary>
/// <see cref="ICodingAgent"/> backed by the open-source Pi coding agent CLI: installs it via npm,
/// launches it in non-interactive JSON event mode, and reads cost from that stream.
/// </summary>
/// <remarks>
/// Like <see cref="OpenCodeAgent"/>, Pi is multi-provider — <see cref="AgentConfig.Model"/> is
/// forwarded verbatim as <c>--model</c> (e.g. <c>openai/gpt-4o</c>), and the caller is responsible
/// for exporting whatever credential env var that provider expects. Pi has no per-run
/// output-token cap equivalent to <c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c>, so
/// <see cref="AgentConfig.MaxTokens"/> is not forwarded and the invocation carries no environment
/// overrides. Unlike OpenCode, Pi does support <c>--append-system-prompt</c>, so rix's system
/// prompt is passed the same way as for <see cref="ClaudeAgent"/>.
/// </remarks>
internal sealed class PiAgent : ICodingAgent
{
    private const string Package = "@earendil-works/pi-coding-agent";

    public Task<InstallResult> EnsureInstalledAsync(RunProcessAsync runProcess, CancellationToken cancellationToken)
    => CodingAgentHelper.EnsureInstalledViaNpmAsync(runProcess, "pi", Package, cancellationToken);

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt)
    {
        List<string> args =
        [
            "--mode", "json", config.Agent.Prompt, "--append-system-prompt", systemPrompt,
        ];
        if (!string.IsNullOrWhiteSpace(config.Agent.Model))
            args.AddRange(["--model", config.Agent.Model]);

        return new
        (
            FileName: "pi",
            Arguments: args,
            EnvironmentOverrides: new Dictionary<string, string>()
        );
    }

    /// <summary>
    /// Reads cost from Pi's JSON event stream. Pi emits per-message cost (<c>usage.cost.total</c>
    /// on each assistant message), not a single cumulative total, so this sums every assistant
    /// message's cost from the <c>agent_end</c> event, which carries the full message list.
    /// </summary>
    /// <remarks>
    /// In practice Pi's actual last stdout line is always a payload-free <c>agent_settled</c>
    /// event that Pi unconditionally emits after <c>agent_end</c> — and <see cref="JobRunner"/>
    /// only ever passes this method the single final line. So this correctly reads cost if ever
    /// given an <c>agent_end</c> line (e.g. if Pi's protocol changes, and for direct unit testing),
    /// but under the current one-line architecture it will reliably return <c>null</c>, and the
    /// run's reported cost will be <c>0</c> — the same kind of known limitation
    /// <see cref="OpenCodeAgent"/> documents for its own frequent zero-cost reports, just total
    /// rather than intermittent.
    /// </remarks>
    public decimal? ParseCost(string outputLine) => CostLine.Read(outputLine, "\"agent_end\"", ReadCost);

    private static decimal? ReadCost(JsonElement root)
    {
        if
        (
            !root.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String || type.GetString() != "agent_end" ||
            !root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array
        )
            return null;

        decimal total = 0m;
        foreach (var message in messages.EnumerateArray())
        {
            if
            (
                message.TryGetProperty("role", out var role) &&
                role.ValueKind == JsonValueKind.String && role.GetString() == "assistant" &&
                message.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
                usage.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object &&
                cost.TryGetProperty("total", out var totalCost) &&
                totalCost.ValueKind == JsonValueKind.Number && totalCost.TryGetDecimal(out var v)
            )
                total += v;
        }
        return total;
    }
}
