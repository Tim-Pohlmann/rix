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

    public async Task<InstallResult> EnsureInstalledAsync(RunProcessAsync runProcess, CancellationToken cancellationToken)
    {
        Task<string?> Run(string fileName, IEnumerable<string> args) =>
            CodingAgentHelper.RunCommandAsync(runProcess, fileName, args, cancellationToken);

        if (await Run("claude", ["--version"]) is null) return new Installed();

        if (await Run("npm", ["--version"]) is { } npmReason)
            return new InstallFailed($"claude is not installed and npm could not be run ({npmReason}). Install Node.js to continue.");

        if (await Run("npm", ["install", "-g", Package]) is { } installReason)
            return new InstallFailed($"npm install -g {Package} failed ({installReason}).");

        // Re-verify: npm install can succeed but claude may still not be on PATH.
        if (await Run("claude", ["--version"]) is { } verifyReason)
            return new InstallFailed($"claude was installed but could not be verified ({verifyReason}).");

        return new Installed();
    }

    public AgentInvocation BuildInvocation(JobConfig config, string systemPrompt) =>
        new(
            FileName: "claude",
            Arguments: ["--output-format", "stream-json", "--print", config.Prompt, "--append-system-prompt", systemPrompt],
            EnvironmentOverrides: new Dictionary<string, string>
            {
                ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
            });

    public decimal? ParseCost(string outputLine) => JobCost.FromResultLine(outputLine);
}
