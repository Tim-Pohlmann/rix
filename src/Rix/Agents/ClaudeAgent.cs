using Rix.Job;
using Rix.Process;

namespace Rix.Agents;

/// <summary>
/// <see cref="ICodingAgent"/> backed by Anthropic's Claude Code CLI: installs it via npm,
/// launches it in streaming-JSON print mode, and reads cost from its NDJSON output.
/// </summary>
internal sealed class ClaudeAgent : ICodingAgent
{
    private const string Package = "@anthropic-ai/claude-code";

    public Task<InstallResult> EnsureInstalledAsync(RunProcessAsync runProcess, CancellationToken cancellationToken) =>
        CodingAgentHelper.EnsureInstalledViaNpmAsync(runProcess, "claude", Package, cancellationToken);

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt) =>
        new(
            FileName: "claude",
            Arguments: ["--output-format", "stream-json", "--print", config.Prompt, "--append-system-prompt", systemPrompt],
            EnvironmentOverrides: new Dictionary<string, string>
            {
                ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
            });

    public decimal? ParseCost(string outputLine) => ClaudeCost.FromResultLine(outputLine);
}
