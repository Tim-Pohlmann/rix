using System.Text.Json.Serialization;

namespace Rix.Submit;

[JsonDerivedType(typeof(SubmitSuccess), "success")]
[JsonDerivedType(typeof(SubmitFailure), "failure")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
internal interface ISubmitResult;

/// <summary>Every pending PR was pushed and opened. <see cref="CreatedPrs"/> lists the branches
/// for which a pull request was created (empty when the job produced no PRs).</summary>
internal sealed record SubmitSuccess
(
    [property: JsonPropertyName("createdPrs")] IReadOnlyList<string> CreatedPrs
) : ISubmitResult;

internal sealed record SubmitFailure
(
    [property: JsonPropertyName("error")] string Error
) : ISubmitResult;

[JsonSerializable(typeof(ISubmitResult))]
[JsonSerializable(typeof(SubmitSuccess))]
[JsonSerializable(typeof(SubmitFailure))]
internal partial class SubmitJsonContext : JsonSerializerContext { }
