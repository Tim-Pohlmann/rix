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
    /// Parses a user-supplied agent name (case-insensitive) into an <see cref="AgentKind"/>. An
    /// empty/whitespace value selects the default (<see cref="AgentKind.Claude"/>); any other
    /// unrecognised value is a <see cref="ParseError{T}"/> for the caller to collect, consistent
    /// with the other <c>Create</c>-time value parsers.
    /// </summary>
    internal static ParseResult<AgentKind> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ParseSuccess<AgentKind>(AgentKind.Claude);
        var normalized = value.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "claude" => new ParseSuccess<AgentKind>(AgentKind.Claude),
            "opencode" => new ParseSuccess<AgentKind>(AgentKind.OpenCode),
            _ => new ParseError<AgentKind>($"unknown agent '{normalized}' (expected 'claude' or 'opencode')"),
        };
    }
}
