using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rix;

internal readonly record struct ReadToken(string Value);
internal readonly record struct WriteToken(string Value);
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

/// <summary>A directory path that is guaranteed to exist as of <see cref="Parse"/>-time. There is no
/// public constructor: an instance can only be obtained through <see cref="Parse"/>, so any
/// <c>DirectoryPath</c> that exists references a directory that existed when it was validated.</summary>
internal sealed record DirectoryPath
{
    internal string Value { get; }

    private DirectoryPath(string value) => Value = value;

    /// <summary>Returns a <see cref="ParseError{T}"/> when the path does not point at an existing
    /// directory, so callers can aggregate it with other validation errors.</summary>
    internal static ParseResult<DirectoryPath> Parse(string path) =>
        Directory.Exists(path)
            ? new ParseSuccess<DirectoryPath>(new DirectoryPath(path))
            : new ParseError<DirectoryPath>($"directory does not exist: {path}");

    public override string ToString() => Value;
}

/// <summary>
/// Base converter for value objects that serialize as a single JSON string. Subclasses supply
/// how to build the wrapper from a string and how to read the string back out. Construction-time
/// validation (an <see cref="ArgumentException"/> from <see cref="Create"/>) is surfaced as a
/// <see cref="JsonException"/> so malformed input fails as a parse error, not an unhandled throw.
/// </summary>
internal abstract class StringValueJsonConverter<T> : JsonConverter<T>
{
    protected abstract T Create(string value);
    protected abstract string Extract(T value);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string token for {typeof(T).Name}, got {reader.TokenType}");
        try { return Create(reader.GetString()!); }
        catch (ArgumentException ex) { throw new JsonException(ex.Message, ex); }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(Extract(value));
}
