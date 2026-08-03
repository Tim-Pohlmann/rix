using System.Text.Json.Serialization;

namespace Rix.Submit;

[JsonDerivedType(typeof(SubmitSuccess), "success")]
[JsonDerivedType(typeof(SubmitFailure), "failure")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
internal interface ISubmitResult;

/// <summary>One pull request that was created by <c>rix submit</c>: its branch and the PR's URL,
/// so callers can link it (e.g. in a job summary) without re-querying the API.</summary>
internal sealed record CreatedPr
(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("url")] string Url
);

/// <summary>Every pending PR was pushed and opened. <see cref="CreatedPrs"/> lists the pull
/// requests that were created (empty when the job produced no PRs).</summary>
internal sealed record SubmitSuccess
(
    [property: JsonPropertyName("createdPrs")] IReadOnlyList<CreatedPr> CreatedPrs
) : ISubmitResult;

internal sealed record SubmitFailure
(
    [property: JsonPropertyName("error")] string Error
) : ISubmitResult;

[JsonSerializable(typeof(ISubmitResult))]
[JsonSerializable(typeof(SubmitSuccess))]
[JsonSerializable(typeof(SubmitFailure))]
internal partial class SubmitJsonContext : JsonSerializerContext { }
