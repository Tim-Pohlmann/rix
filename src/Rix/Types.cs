using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rix;

internal readonly record struct ReadToken(string Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);

[JsonConverter(typeof(PrTitleJsonConverter))]
internal readonly record struct PrTitle(string Value);

[JsonConverter(typeof(PrBodyJsonConverter))]
internal readonly record struct PrBody(string Value);

internal readonly record struct RepoIdentifier
{
    internal string Value { get; }

    internal RepoIdentifier(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
            throw new ArgumentException($"'{value}' is not a valid repo identifier; expected owner/name format.", nameof(value));
        Value = value;
    }

    public override string ToString() => Value;
}

[JsonConverter(typeof(BranchNameJsonConverter))]
internal record BranchName(string Value)
{
    public override string ToString() => Value;
}

[JsonConverter(typeof(RixBranchNameJsonConverter))]
internal record RixBranchName : BranchName
{
    private static readonly Regex Pattern =
        new(@"^rix/.+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    internal RixBranchName(string value) : base(value)
    {
        if (!Pattern.IsMatch(value))
            throw new ArgumentException($"Branch must match rix/* pattern, got: {value}", nameof(value));
    }
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

internal sealed class BranchNameJsonConverter : StringValueJsonConverter<BranchName>
{
    protected override BranchName Create(string value) => new(value);
    protected override string Extract(BranchName value) => value.Value;
}

internal sealed class RixBranchNameJsonConverter : StringValueJsonConverter<RixBranchName>
{
    protected override RixBranchName Create(string value) => new(value);
    protected override string Extract(RixBranchName value) => value.Value;
}

internal sealed class PrTitleJsonConverter : StringValueJsonConverter<PrTitle>
{
    protected override PrTitle Create(string value) => new(value);
    protected override string Extract(PrTitle value) => value.Value;
}

internal sealed class PrBodyJsonConverter : StringValueJsonConverter<PrBody>
{
    protected override PrBody Create(string value) => new(value);
    protected override string Extract(PrBody value) => value.Value;
}

internal record QueuedPr(RixBranchName Branch, BranchName BaseBranch, PrTitle Title, PrBody Body);

internal record PendingPr(
    [property: JsonPropertyName("branch")] RixBranchName Branch,
    [property: JsonPropertyName("baseBranch")] BranchName BaseBranch,
    [property: JsonPropertyName("title")] PrTitle Title,
    [property: JsonPropertyName("body")] PrBody Body,
    [property: JsonPropertyName("bundleFile")] string BundleFile
);
