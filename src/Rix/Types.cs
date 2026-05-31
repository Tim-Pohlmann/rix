using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rix;

internal readonly record struct ReadToken(string Value);
internal readonly record struct WriteToken(string Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);

internal readonly record struct RepoIdentifier
{
    internal string Value { get; }

    internal RepoIdentifier(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            var slash = value.IndexOf('/');
            if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
                throw new ArgumentException($"'{value}' is not a valid repo identifier; expected owner/name format.", nameof(value));
        }
        Value = value;
    }

    public override string ToString() => Value;
}

[JsonConverter(typeof(BranchNameJsonConverter))]
internal readonly record struct BranchName(string Value);

internal static class BranchNameExtensions
{
    private static readonly Regex Pattern =
        new(@"^rix/.+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    extension(BranchName branch)
    {
        public bool Valid => Pattern.IsMatch(branch.Value);
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

internal record PullRequest(
    [property: JsonPropertyName("url")] Uri Url,
    [property: JsonPropertyName("branch")] BranchName Branch
);

[JsonDerivedType(typeof(JobSuccess), "success")]
[JsonDerivedType(typeof(JobFailure), "failure")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
internal interface IJobResult
{
    int TokensUsed { get; }
    TimeSpan Duration { get; }
    int DurationSeconds { get; }
}

internal record JobSuccess(
    [property: JsonPropertyName("prs")] IReadOnlyList<PullRequest> Prs,
    [property: JsonPropertyName("tokensUsed")] int TokensUsed,
    [property: JsonIgnore] TimeSpan Duration
) : IJobResult
{
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds => (int)Duration.TotalSeconds;
}

internal record JobFailure(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("tokensUsed")] int TokensUsed,
    [property: JsonIgnore] TimeSpan Duration
) : IJobResult
{
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds => (int)Duration.TotalSeconds;
}
