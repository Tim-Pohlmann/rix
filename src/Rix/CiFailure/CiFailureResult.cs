using System.Text.Json.Serialization;

namespace Rix.CiFailure;

[JsonDerivedType(typeof(CiFailureDetected), "detected")]
[JsonDerivedType(typeof(CiFailureSkipped), "skipped")]
[JsonDerivedType(typeof(CiFailureError), "error")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
internal interface ICiFailureResult;

/// <summary>The run failed: a ready-to-use prompt plus the raw facts it was built from, so a
/// caller that wants a different prompt shape isn't forced to re-fetch them.</summary>
internal sealed record CiFailureDetected
(
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("runUrl")] string RunUrl,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("prNumber")] int? PrNumber
) : ICiFailureResult;

/// <summary>The run did not fail (e.g. it succeeded, was cancelled, or is still in progress) —
/// nothing to do.</summary>
internal sealed record CiFailureSkipped
(
    [property: JsonPropertyName("conclusion")] string Conclusion
) : ICiFailureResult;

/// <summary>Something went wrong fetching or interpreting the run's data, as opposed to the run
/// itself having failed — e.g. a bad token or an unreachable API.</summary>
internal sealed record CiFailureError
(
    [property: JsonPropertyName("error")] string Error
) : ICiFailureResult;

[JsonSerializable(typeof(ICiFailureResult))]
[JsonSerializable(typeof(CiFailureDetected))]
[JsonSerializable(typeof(CiFailureSkipped))]
[JsonSerializable(typeof(CiFailureError))]
internal partial class CiFailureJsonContext : JsonSerializerContext { }
