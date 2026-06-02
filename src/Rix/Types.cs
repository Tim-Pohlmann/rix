using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rix;

internal readonly record struct ReadToken(string Value);
internal readonly record struct WriteToken(string Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);
internal readonly record struct PrTitle(string Value);
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

internal sealed class BranchNameJsonConverter : JsonConverter<BranchName>
{
    public override BranchName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string token for BranchName, got {reader.TokenType}");
        return new(reader.GetString()!);
    }
    public override void Write(Utf8JsonWriter writer, BranchName value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal sealed class RixBranchNameJsonConverter : JsonConverter<RixBranchName>
{
    public override RixBranchName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Expected string token for RixBranchName, got {reader.TokenType}");
        try { return new(reader.GetString()!); }
        catch (ArgumentException ex) { throw new JsonException(ex.Message, ex); }
    }
    public override void Write(Utf8JsonWriter writer, RixBranchName value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}

internal record PendingPr(
    RixBranchName Branch,
    BranchName BaseBranch,
    PrTitle Title,
    PrBody Body
);
