using System.Text.Json.Serialization;

namespace Rix.CiFailure;

[JsonDerivedType(typeof(CiFailureDetected), "detected")]
[JsonDerivedType(typeof(CiFailureSkipped), "skipped")]
[JsonDerivedType(typeof(CiFailureLoopGuarded), "loopGuarded")]
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

/// <summary>The run did not fail (e.g. it succeeded, was cancelled, or is still in progress, in
/// which case <paramref name="Conclusion"/> is <c>null</c>) — nothing to do.</summary>
internal sealed record CiFailureSkipped
(
    [property: JsonPropertyName("conclusion")] string? Conclusion
) : ICiFailureResult;

/// <summary>The run failed, but the tip of its branch is already <paramref name="RixCommits"/> of
/// rix's own commits in a row — rix reacting to a failure of its own last attempt. Answering again
/// would extend a loop that has already had its chances, so it stops here and leaves the branch to
/// a human, whose next commit to it clears the streak and re-enables rix on its own.</summary>
internal sealed record CiFailureLoopGuarded
(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("rixCommits")] int RixCommits
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
[JsonSerializable(typeof(CiFailureLoopGuarded))]
[JsonSerializable(typeof(CiFailureError))]
internal partial class CiFailureJsonContext : JsonSerializerContext { }
