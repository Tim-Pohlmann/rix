using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rix;

internal readonly record struct ReadToken(string Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);

internal readonly record struct RepoIdentifier(string Value)
{
    /// <summary>The single source of truth for the owner/name format rule. Returns a
    /// human-readable error message, or <c>null</c> when <paramref name="value"/> is well-formed.
    /// Centralizing the check here lets <see cref="Rix.Job.JobConfig"/> surface a malformed repo
    /// alongside its other validation errors instead of failing on construction.</summary>
    internal static string? FormatError(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
            return $"'{value}' is not a valid repo identifier; expected owner/name format.";
        return null;
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
