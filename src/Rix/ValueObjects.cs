using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rix;

internal readonly record struct ReadToken(string Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);

/// <summary>The outcome of parsing raw text into a <see cref="RepoIdentifier"/>: either the parsed
/// value (<see cref="ParsedRepo"/>) or a human-readable error (<see cref="RepoParseError"/>).
/// Pattern-matched at the validation boundary so a malformed input becomes a collectable error
/// rather than a thrown exception.</summary>
internal abstract record RepoParseResult
{
    private protected RepoParseResult() { }
}

internal sealed record ParsedRepo(RepoIdentifier Value) : RepoParseResult;

internal sealed record RepoParseError(string Error) : RepoParseResult;

/// <summary>A validated GitHub <c>owner/name</c> identifier. There is no public constructor: an
/// instance can only be obtained through <see cref="Parse"/>, so any <c>RepoIdentifier</c> that
/// exists is guaranteed well-formed. Raw, not-yet-validated input is carried as a plain
/// <c>string</c> until <see cref="Rix.Job.JobConfig.Create"/> parses it.</summary>
internal sealed record RepoIdentifier
{
    internal string Value { get; }

    private RepoIdentifier(string value) => Value = value;

    /// <summary>The single source of truth for the owner/name format rule. Returns a
    /// <see cref="RepoParseError"/> for malformed input instead of constructing an invalid instance,
    /// so callers can aggregate it with other validation errors.</summary>
    internal static RepoParseResult Parse(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
            return new RepoParseError($"'{value}' is not a valid repo identifier; expected owner/name format.");
        return new ParsedRepo(new RepoIdentifier(value));
    }

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
