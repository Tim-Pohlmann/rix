using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rix;

/// <summary>The outcome of parsing raw text into a strongly-typed value: either the parsed
/// <see cref="ParseSuccess{T}"/> or a human-readable <see cref="ParseError{T}"/>. Pattern-matched at
/// the validation boundary so malformed input becomes a collectable error rather than a thrown
/// exception or an invalid instance. <typeparamref name="T"/> is unused in this base (Sonar S2326,
/// accepted) — it exists only so the success and error cases share one generic union.</summary>
[SuppressMessage
(
    "Major Code Smell", "S2326:Unused type parameters should be removed",
    Justification = "T parameterises the success/error cases that derive from this union root.")]
internal abstract record ParseResult<T>
{
    private protected ParseResult() { }

    /// <summary>Eliminates the union: runs <paramref name="onSuccess"/> for a
    /// <see cref="ParseSuccess{T}"/> or <paramref name="onError"/> for a <see cref="ParseError{T}"/>.
    /// The <c>private protected</c> ctor keeps these the only two cases in practice (new ones could
    /// only be added here, in this assembly), so callers fold without repeating the catch-all arm —
    /// which this method keeps in one place as a guard rather than spread across every site.</summary>
    internal TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onError) => this switch
    {
        ParseSuccess<T> ok => onSuccess(ok.Value),
        ParseError<T> bad => onError(bad.Error),
        _ => throw new InvalidOperationException($"Unexpected {nameof(ParseResult<T>)}: {GetType().Name}"),
    };
}

internal sealed record ParseSuccess<T>(T Value) : ParseResult<T>;

/// <summary>The failure case carries only a message; <typeparamref name="T"/> exists purely to keep
/// it in the same union as <see cref="ParseSuccess{T}"/> so callers can switch over one type. The
/// resulting "unused type parameter" smell (Sonar S2326) is accepted by design.</summary>
[SuppressMessage
(
    "Major Code Smell", "S2326:Unused type parameters should be removed",
    Justification = "T keeps the error case in the same ParseResult<T> union as the success case.")]
internal sealed record ParseError<T>(string Error) : ParseResult<T>;

/// <summary>The shared validation strategy for the command <c>Create</c> methods: every parsed field
/// folds through <see cref="Collect{T}"/> so each error accumulates in one pass with the same
/// <c>flag: message</c> shape, instead of each command repeating its own success/error switch.</summary>
internal static class ParseResultExtensions
{
    /// <summary>Unwraps a successful parse to its value; on failure records <paramref name="flag"/>
    /// and the error in <paramref name="errors"/> and returns <c>null</c> so the caller keeps
    /// collecting the remaining fields before deciding the config is invalid.</summary>
    internal static T? Collect<T>(this ParseResult<T> result, List<string> errors, string flag) where T : class
    => result.Match<T?>(value => value, error => { errors.Add($"{flag}: {error}"); return null; });
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
        return Parse(reader.GetString()!).Match(onSuccess: value => value, onError: error => throw new JsonException(error));
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    => writer.WriteStringValue(Extract(value));
}
