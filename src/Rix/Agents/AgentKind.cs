namespace Rix.Agents;

/// <summary>Selects which <see cref="ICodingAgent"/> implementation a job runs with.</summary>
internal enum AgentKind
{
    /// <summary>Anthropic's Claude Code CLI (<see cref="ClaudeAgent"/>) — the default.</summary>
    Claude,

    /// <summary>The open-source OpenCode CLI (<see cref="OpenCodeAgent"/>).</summary>
    OpenCode,
}

internal static class AgentKindParser
{
    /// <summary>
    /// Parses a user-supplied agent name (case-insensitive) into an <see cref="AgentKind"/>.
    /// An empty/whitespace value selects the default (<see cref="AgentKind.Claude"/>); any other
    /// unrecognised value throws so the CLI can surface a clear error.
    /// </summary>
    internal static AgentKind Parse(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? AgentKind.Claude
            : value.Trim().ToLowerInvariant() switch
            {
                "claude" => AgentKind.Claude,
                "opencode" => AgentKind.OpenCode,
                _ => throw new ArgumentException($"unknown agent '{value}' (expected 'claude' or 'opencode')"),
            };
}
