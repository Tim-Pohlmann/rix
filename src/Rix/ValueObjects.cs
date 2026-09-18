using System.Numerics;

namespace Rix;

/// <summary>Writes a single diagnostic line (e.g. a forwarded agent stdout line) to the log sink.</summary>
internal delegate void LogLine(string line);

/// <summary>Shared numeric-flag parsing used by every <c>Config.Create</c>: blank input either
/// resolves to <paramref name="defaultValue"/> (an optional flag, e.g. <c>--max-tokens</c>) or is
/// itself an error when <paramref name="defaultValue"/> is <c>null</c> (a required flag, e.g.
/// <c>--run-id</c>) — either way, anything non-blank must parse as a positive number or
/// <paramref name="errors"/> gets a message naming exactly what was wrong, rather than silently
/// falling back to a default and hiding a caller's typo.</summary>
internal static class NumericFlag
{
    internal static T ParsePositiveInt<T>(string? raw, T? defaultValue, string flag, List<string> errors)
        where T : struct, INumber<T>
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (defaultValue is { } value)
                return value;
            errors.Add($"{flag} is required");
            return default;
        }
        if (!T.TryParse(raw, null, out var parsed) || parsed <= T.Zero)
        {
            errors.Add($"{flag} must be a positive integer, got '{raw}'");
            return defaultValue ?? default;
        }
        return parsed;
    }
}

/// <summary>A read-scoped GitHub access token: enough to clone and inspect a repo, never to write.
/// <see cref="GitToken"/> derives from it because a write-capable token can do everything a read
/// one can, so a <see cref="GitToken"/> is accepted wherever a <c>GitReadToken</c> is required.</summary>
internal record GitReadToken(string Value);

/// <summary>A write-capable GitHub access token (push, open PRs). Being a <see cref="GitReadToken"/>,
/// it also satisfies read-only consumers without a separate credential.</summary>
internal sealed record GitToken(string Value) : GitReadToken(Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);

/// <summary>The GitHub Actions identifier of a single workflow run. A distinct type rather than a
/// bare <c>long</c> so it can't be transposed with the other numbers threaded through the same
/// calls (a PR number, a job count).</summary>
internal readonly record struct RunId(long Value);

/// <summary>A validated GitHub <c>owner/name</c> identifier. There is no public constructor: an
/// instance can only be obtained through <see cref="Parse"/>, so any <c>RepoIdentifier</c> that
/// exists is guaranteed well-formed. Raw, not-yet-validated input is carried as a plain
/// <c>string</c> until <see cref="Rix.Job.JobConfig.Create"/> parses it.</summary>
internal sealed record RepoIdentifier
{
    internal string Value { get; }

    /// <summary>The <c>owner</c> segment, needed to scope a pull-request lookup to same-repo
    /// branches: GitHub's <c>/pulls?head=</c> filter matches <c>owner:branch</c>, not branch name
    /// alone. Split off once in <see cref="Parse"/>, which has already located the separator,
    /// rather than re-scanning <see cref="Value"/> on every read.</summary>
    internal string Owner { get; }

    private RepoIdentifier(string value, string owner)
    {
        Value = value;
        Owner = owner;
    }

    /// <summary>The single source of truth for the owner/name format rule. Returns a
    /// <see cref="ParseError{T}"/> for malformed input instead of constructing an invalid instance,
    /// so callers can aggregate it with other validation errors.</summary>
    internal static ParseResult<RepoIdentifier> Parse(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
            return new ParseError<RepoIdentifier>($"'{value}' is not a valid repo identifier; expected owner/name format.");
        return new ParseSuccess<RepoIdentifier>(new RepoIdentifier(value, value[..slash]));
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
    {
        if (Directory.Exists(path))
            return new ParseSuccess<DirectoryPath>(new DirectoryPath(Path.GetFullPath(path)));
        return new ParseError<DirectoryPath>($"directory does not exist: {path}");
    }

    public override string ToString() => Value;
}
