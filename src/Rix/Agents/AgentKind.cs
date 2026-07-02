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
    /// Throwing convenience over <see cref="TryParse"/>: parses a user-supplied agent name
    /// (case-insensitive) into an <see cref="AgentKind"/>. An empty/whitespace value selects the
    /// default (<see cref="AgentKind.Claude"/>); any other unrecognised value throws.
    /// </summary>
    internal static AgentKind Parse(string? value)
    => TryParse(value, out var kind, out var error) switch
    {
        true => kind,
        false => throw new ArgumentException(error)
    };

    /// <summary>
    /// Non-throwing variant for the error-collecting validation path: returns <c>false</c> with a
    /// human-readable <paramref name="error"/> for an unrecognised value, leaving <paramref name="kind"/>
    /// at the default. An empty/whitespace value selects the default (<see cref="AgentKind.Claude"/>).
    /// </summary>
    internal static bool TryParse(string? value, out AgentKind kind, out string? error)
    {
        error = null;
        kind = AgentKind.Claude;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim();
        switch (normalized.ToLowerInvariant())
        {
            case "claude":
                return true;
            case "opencode":
                kind = AgentKind.OpenCode;
                return true;
            default:
                error = $"unknown agent '{normalized}' (expected 'claude' or 'opencode')";
                return false;
        }
    }
}
