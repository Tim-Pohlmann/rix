using System.Text.Json.Serialization;

namespace Rix.CiFailure;

[JsonDerivedType(typeof(CiFailureDetected), "detected")]
[JsonDerivedType(typeof(CiFailureSkipped), "skipped")]
[JsonDerivedType(typeof(CiFailureLoopGuarded), "loopGuarded")]
[JsonDerivedType(typeof(CiFailureUntrustedRun), "untrustedRun")]
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

/// <summary>The run failed, but its branch lives in <paramref name="HeadRepo"/> rather than in the
/// watched repo — a fork's pull request. Nobody needs write access to the watched repo to push to a
/// fork, so the code, the failing test and the log output that would become the agent's prompt are
/// all attacker-controlled, while the workflow answering them holds the watched repo's write token.
/// Rix therefore doesn't answer these at all; who can push to the repo is the permission check it
/// defers to, rather than asking the API about the triggering account.
///
/// A maintainer's own fork PR is turned away by the same rule. That costs nothing: its branch
/// doesn't exist in the watched repo, so the clone rix would push a fix onto has nowhere to come
/// from — the fork PR is outside what rix can act on either way.</summary>
internal sealed record CiFailureUntrustedRun
(
    [property: JsonPropertyName("headRepo")] string HeadRepo,
    [property: JsonPropertyName("branch")] string Branch
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
[JsonSerializable(typeof(CiFailureUntrustedRun))]
[JsonSerializable(typeof(CiFailureError))]
internal partial class CiFailureJsonContext : JsonSerializerContext { }
