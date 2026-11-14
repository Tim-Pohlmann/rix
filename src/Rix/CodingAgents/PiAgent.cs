using Rix.Agent;
using Rix.Process;
using System.Text.Json;

namespace Rix.CodingAgents;

/// <summary>
/// <see cref="ICodingAgent"/> backed by the open-source Pi coding agent CLI: installs it via npm,
/// launches it in non-interactive JSON event mode, and reads cost from that stream.
/// </summary>
/// <remarks>
/// Like <see cref="OpenCodeAgent"/>, Pi is multi-provider — <see cref="CodingAgentConfig.Model"/> is
/// forwarded verbatim as <c>--model</c> (e.g. <c>openai/gpt-4o</c>), and <see cref="Agent.AgentRunner"/>
/// adds whatever credential env var that provider expects to this invocation's environment (see
/// <see cref="AgentCredential"/>), so this method itself carries no override for it. Pi has no
/// per-run output-token cap equivalent to <c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c>, so
/// <see cref="CodingAgentConfig.MaxTokens"/> is not forwarded either. Unlike OpenCode, Pi does support
/// <c>--append-system-prompt</c>, so rix's system prompt is passed the same way as for
/// <see cref="ClaudeAgent"/>.
/// </remarks>
internal sealed class PiAgent : ICodingAgent
{
    private const string Package = "@earendil-works/pi-coding-agent";

    public Task<InstallResult> EnsureInstalledAsync
    (
        IProcessRunner processRunner,
        DirectoryPath workingDirectory,
        Action<string> reportDiagnostic,
        CancellationToken cancellationToken
    )
    => CodingAgentHelper.EnsureInstalledViaNpmAsync
    (
        processRunner, workingDirectory, "pi", Package, reportDiagnostic, cancellationToken
    );

    public AgentInvocation BuildInvocation(AgentConfig config, string systemPrompt)
    {
        List<string> args =
        [
            "--mode", "json", config.Agent.Prompt, "--append-system-prompt", systemPrompt,
        ];
        if (!string.IsNullOrWhiteSpace(config.Agent.Model))
            args.AddRange(["--model", config.Agent.Model]);
        // Pi calls its reasoning-effort setting the "thinking level".
        if (!string.IsNullOrWhiteSpace(config.Agent.Effort))
            args.AddRange(["--thinking", config.Agent.Effort]);

        return new
        (
            "pi",
            args,
            new Dictionary<string, string>()
        );
    }

    /// <summary>
    /// Reads cost from Pi's JSON event stream. Pi emits per-message cost (<c>usage.cost.total</c>
    /// on each assistant message), not a single cumulative total, so this sums every assistant
    /// message's cost from an <c>agent_end</c> event, which carries the message list of one agent
    /// run. A job can see several: a retry, or the run that resumes after compacting an overflowed
    /// context, ends with an <c>agent_end</c> of its own that lists only its own messages, so each
    /// one's cost adds to the job's.
    /// </summary>
    public decimal? ParseCost(string outputLine) => JsonLine.Read(outputLine, "\"agent_end\"", ReadCost);

    /// <summary>
    /// Reads transcript content from Pi's JSON event stream, reusing the <c>agent_end</c> line that
    /// <see cref="ParseCost"/> already relies on: every assistant message's text and tool-call
    /// content is rendered into one combined chunk. Other lines yield <c>null</c>.
    /// </summary>
    public string? ParseTranscriptLine(string outputLine) => JsonLine.Read(outputLine, "\"agent_end\"", ReadTranscript);

    private static decimal? ReadCost(JsonElement root)
    {
        if (ReadAgentEndMessages(root) is not { } messages)
            return null;

        decimal total = 0m;
        foreach (var message in messages.EnumerateArray())
            total += ReadAssistantCost(message);
        return total;
    }

    /// <summary>The message list of an <c>agent_end</c> event, or <c>null</c> for any other line.
    /// The marker <see cref="JsonLine"/> matches on is only a substring - an assistant message
    /// quoting it would pass - and a line counted twice would bill its run twice.</summary>
    private static JsonElement? ReadAgentEndMessages(JsonElement root)
    {
        if
        (
            root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "agent_end" &&
            root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array
        )
            return messages;
        return null;
    }

    private static decimal ReadAssistantCost(JsonElement message)
    {
        if (!message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String || role.GetString() != "assistant")
            return 0m;
        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return 0m;
        if (!usage.TryGetProperty("cost", out var cost) || cost.ValueKind != JsonValueKind.Object)
            return 0m;
        if (!cost.TryGetProperty("total", out var totalCost) || totalCost.ValueKind != JsonValueKind.Number)
            return 0m;
        if (!totalCost.TryGetDecimal(out var v))
            return 0m;
        return v;
    }

    private static string? ReadTranscript(JsonElement root)
    {
        if (ReadAgentEndMessages(root) is not { } messages)
            return null;

        return TranscriptLine.JoinNonNull(messages.EnumerateArray().Select(ReadAssistantTranscript));
    }

    private static string? ReadAssistantTranscript(JsonElement message)
    {
        if
        (
            message.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String && role.GetString() == "assistant" &&
            message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
        )
            return TranscriptLine.JoinContentBlocks(content, "toolCall", "arguments");
        return null;
    }
}
