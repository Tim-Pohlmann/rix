using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>The outcome of parsing raw text into a strongly-typed value: either the parsed
/// <see cref="ParseSuccess{T}"/> or a human-readable <see cref="ParseError{T}"/>. Pattern-matched at
/// the validation boundary so malformed input becomes a collectable error rather than a thrown
/// exception or an invalid instance. <typeparamref name="T"/> is unused in this base (Sonar S2326,
/// accepted) — it exists only so the success and error cases share one generic union.</summary>
[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed",
    Justification = "T parameterises the success/error cases that derive from this union root.")]
internal abstract record ParseResult<T>
{
    private protected ParseResult() { }
}

internal sealed record ParseSuccess<T>(T Value) : ParseResult<T>;

/// <summary>The failure case carries only a message; <typeparamref name="T"/> exists purely to keep
/// it in the same union as <see cref="ParseSuccess{T}"/> so callers can switch over one type. The
/// resulting "unused type parameter" smell (Sonar S2326) is accepted by design.</summary>
[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed",
    Justification = "T keeps the error case in the same ParseResult<T> union as the success case.")]
internal sealed record ParseError<T>(string Error) : ParseResult<T>;

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
    internal static ParseResult<DirectoryPath> Parse(string path) =>
        Directory.Exists(path)
            ? new ParseSuccess<DirectoryPath>(new DirectoryPath(Path.GetFullPath(path)))
            : new ParseError<DirectoryPath>($"directory does not exist: {path}");

    public override string ToString() => Value;
}

/// <summary>
/// Base converter for value objects that serialize as a single JSON string. Subclasses supply how
/// to <see cref="Parse"/> a string into the wrapper and how to read the string back out. A
/// <see cref="ParseError{T}"/> is surfaced as a <see cref="JsonException"/> so malformed input fails
/// as a parse error rather than an unhandled throw — validation stays on the union, not exceptions.
/// </summary>
internal abstract class StringValueJsonConverter<T> : JsonConverter<T>
{
    protected abstract ParseResult<T> Parse(string value);
    protected abstract string Extract(T value);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string token for {typeof(T).Name}, got {reader.TokenType}");
        return Parse(reader.GetString()!) switch
        {
            ParseSuccess<T> ok => ok.Value,
            ParseError<T> bad => throw new JsonException(bad.Error),
            var other => throw new JsonException($"Unexpected parse result for {typeof(T).Name}: {other.GetType().Name}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(Extract(value));
}
