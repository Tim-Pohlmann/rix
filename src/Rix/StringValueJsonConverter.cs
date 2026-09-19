using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rix;

/// <summary>Base converter for value objects that serialize as a single JSON string. Subclasses
/// supply how to construct the wrapper from a string and how to read the string back out. An
/// <see cref="InvalidInputException"/> from the constructor is surfaced as a
/// <see cref="JsonException"/>, the failure the deserializing caller already expects for malformed
/// input (e.g. <see cref="Submit.SubmitRunner"/> reading <c>result.json</c>).</summary>
internal abstract class StringValueJsonConverter<T>(Func<string, T> construct, Func<T, string> extract) : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string token for {typeof(T).Name}, got {reader.TokenType}");
        try
        {
            return construct(reader.GetString()!);
        }
        catch (InvalidInputException ex)
        {
            throw new JsonException(ex.Message, ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    => writer.WriteStringValue(extract(value));
}
