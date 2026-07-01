namespace Rix;

/// <summary>Writes a single diagnostic line (e.g. a forwarded agent stdout line) to the log sink.</summary>
internal delegate void LogLine(string line);

/// <summary>A read-scoped GitHub access token: enough to clone and inspect a repo, never to write.
/// <see cref="GitToken"/> derives from it because a write-capable token can do everything a read
/// one can, so a <see cref="GitToken"/> is accepted wherever a <c>GitReadToken</c> is required.</summary>
internal record GitReadToken(string Value);

/// <summary>A write-capable GitHub access token (push, open PRs). Being a <see cref="GitReadToken"/>,
/// it also satisfies read-only consumers without a separate credential.</summary>
internal sealed record GitToken(string Value) : GitReadToken(Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);

/// <summary>A validated GitHub <c>owner/name</c> identifier. There is no public constructor: an
/// instance can only be obtained through <see cref="Parse"/>, so any <c>RepoIdentifier</c> that
/// exists is guaranteed well-formed. Raw, not-yet-validated input is carried as a plain
/// <c>string</c> until <see cref="Rix.Job.JobConfig.Create"/> parses it.</summary>
internal sealed record RepoIdentifier
{
    internal string Value { get; }

    private RepoIdentifier(string value) => Value = value;

    /// <summary>The single source of truth for the owner/name format rule. Returns a
    /// <see cref="ParseError{T}"/> for malformed input instead of constructing an invalid instance,
    /// so callers can aggregate it with other validation errors.</summary>
    internal static ParseResult<RepoIdentifier> Parse(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
            return new ParseError<RepoIdentifier>($"'{value}' is not a valid repo identifier; expected owner/name format.");
        return new ParseSuccess<RepoIdentifier>(new RepoIdentifier(value));
    }

    public override string ToString() => Value;
}

/// <summary>A directory path that is guaranteed to exist as of <see cref="Parse"/>-time and is
/// stored as an absolute path. There is no public constructor: an instance can only be obtained
/// through <see cref="Parse"/>, so any <c>DirectoryPath</c> that exists references a directory that
/// existed when it was validated. Normalising to absolute at the boundary means paths derived from
/// it (e.g. via <see cref="System.IO.Path.Combine(string, string)"/>) stay rooted, so a subprocess
/// run from a different working directory resolves them where the caller intended.</summary>
internal sealed record DirectoryPath
{
    internal string Value { get; }

    private DirectoryPath(string value) => Value = value;

    /// <summary>Returns a <see cref="ParseError{T}"/> when the path does not point at an existing
    /// directory, so callers can aggregate it with other validation errors. On success the path is
    /// normalised to absolute via <see cref="System.IO.Path.GetFullPath(string)"/>.</summary>
    internal static ParseResult<DirectoryPath> Parse(string path)
    => Directory.Exists(path) switch
    {
        true => new ParseSuccess<DirectoryPath>(new DirectoryPath(Path.GetFullPath(path))),
        false => new ParseError<DirectoryPath>($"directory does not exist: {path}")
    };

    public override string ToString() => Value;
}
