using Rix.Job;
using Rix.Process;

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
/// <c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c>, so <see cref="JobConfig.MaxTokens"/> is not forwarded;
/// the invocation carries no environment overrides.</item>
/// </list>
/// </remarks>
internal sealed class OpenCodeAgent : ICodingAgent
{
    private const string Package = "opencode-ai";

    public Task<InstallResult> EnsureInstalledAsync(RunProcessAsync runProcess, CancellationToken cancellationToken) =>
        CodingAgentHelper.EnsureInstalledViaNpmAsync(runProcess, "opencode", Package, cancellationToken);

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt) =>
        new(
            FileName: "opencode",
            Arguments: ["run", $"{systemPrompt}\n\n{config.Agent.Prompt}", "--format", "json"],
            EnvironmentOverrides: new Dictionary<string, string>());

    public decimal? ParseCost(string outputLine) => OpenCodeCost.FromEventLine(outputLine);
}
