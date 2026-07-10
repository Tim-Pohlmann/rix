namespace Rix.Agents;

/// <summary>Selects which <see cref="ICodingAgent"/> implementation a job runs with.</summary>
internal enum AgentKind
{
    /// <summary>Anthropic's Claude Code CLI (<see cref="ClaudeAgent"/>).</summary>
    Claude,

    /// <summary>The open-source, multi-provider OpenCode CLI (<see cref="OpenCodeAgent"/>) — the default.</summary>
    OpenCode,

    /// <summary>The open-source, multi-provider Pi coding agent CLI (<see cref="PiAgent"/>).</summary>
    Pi,
}

internal static class AgentKindParser
{
    /// <summary>
    /// Parses a non-blank, user-supplied agent name (case-insensitive) into an <see cref="AgentKind"/>,
    /// or a <see cref="ParseError{T}"/> for the caller to collect. Whether a blank input should select
    /// a default is the caller's policy to decide, not this parser's — consistent with how
    /// <see cref="Job.JobConfig.Create"/> resolves its other optional fields.
    /// </summary>
    internal static ParseResult<AgentKind> Parse(string value)
    {
        var normalized = value.Trim();
        return normalized.ToLowerInvariant() switch
        {
            "claude" => new ParseSuccess<AgentKind>(AgentKind.Claude),
            "opencode" => new ParseSuccess<AgentKind>(AgentKind.OpenCode),
            "pi" => new ParseSuccess<AgentKind>(AgentKind.Pi),
            _ => new ParseError<AgentKind>($"unknown agent '{normalized}' (expected 'claude', 'opencode', or 'pi')"),
        };
    }
}
