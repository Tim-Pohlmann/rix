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

/// <summary>One already-submitted task whose pull request <c>rix submit</c> updated (title/body):
/// its branch and the PR's URL. Shares the <see cref="CreatedPr"/> shape, so it renders in the
/// job summary the same way.</summary>
internal sealed record UpdatedPr
(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("url")] string Url
);

/// <summary>One already-submitted task whose pull request <c>rix submit</c> reverted (closed):
/// its branch and the PR's URL. Shares the <see cref="CreatedPr"/> shape.</summary>
internal sealed record ClosedPr
(
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("url")] string Url
);

/// <summary>Every pending PR was pushed and opened, every pending push was delivered, and every
/// pending task update/revert was applied. <see cref="CreatedPrs"/> lists the pull requests that
/// were created (empty when the job produced no PRs); <see cref="PushedBranches"/> lists the
/// branches commits were pushed to via <c>/push</c> (empty when the job queued no pushes);
/// <see cref="UpdatedPrs"/> and <see cref="ClosedPrs"/> list the already-submitted tasks that were
/// updated or reverted via <c>/tasks/update</c> and <c>/tasks/revert</c>.</summary>
internal sealed record SubmitSuccess
(
    [property: JsonPropertyName("createdPrs")] IReadOnlyList<CreatedPr> CreatedPrs,
    [property: JsonPropertyName("pushedBranches")] IReadOnlyList<string> PushedBranches,
    [property: JsonPropertyName("updatedPrs")] IReadOnlyList<UpdatedPr> UpdatedPrs,
    [property: JsonPropertyName("closedPrs")] IReadOnlyList<ClosedPr> ClosedPrs
) : ISubmitResult;

internal sealed record SubmitFailure
(
    [property: JsonPropertyName("error")] string Error
) : ISubmitResult;

[JsonSerializable(typeof(ISubmitResult))]
[JsonSerializable(typeof(SubmitSuccess))]
[JsonSerializable(typeof(SubmitFailure))]
internal partial class SubmitJsonContext : JsonSerializerContext { }