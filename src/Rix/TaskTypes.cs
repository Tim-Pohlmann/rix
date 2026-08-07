using System.Text.Json.Serialization;

namespace Rix;

/// <summary>A pull request already opened on the remote, as observed through the read host. The
/// head <see cref="Branch"/> is a plain string because it is a remote fact, not an input the agent
/// validated — the <c>rix/*</c> convention is applied by callers that only care about the factory's
/// own tasks.</summary>
internal sealed record RemotePr
(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("baseBranch")] string BaseBranch,
    [property: JsonPropertyName("url")] string Url
);

/// <summary>A request to update an already-submitted task (an open pull request): change its
/// <see cref="Title"/> and/or <see cref="Body"/>. No bundle is produced — the submit step locates
/// the PR by <see cref="Branch"/> and patches it with the write credential.</summary>
internal record QueuedTaskUpdate(RixBranchName Branch, PrTitle? Title, PrBody? Body);

/// <summary>The deliverable form of a <see cref="QueuedTaskUpdate"/>, carried in <c>result.json</c>
/// so <c>rix submit</c> can apply it. Identical shape to the queued form — kept as a separate type
/// to mirror the queued/pending split of PRs and pushes.</summary>
internal record PendingTaskUpdate
(
    [property: JsonPropertyName("branch")] RixBranchName Branch,
    [property: JsonPropertyName("title")] PrTitle? Title,
    [property: JsonPropertyName("body")] PrBody? Body
);

/// <summary>A request to revert an already-submitted task: close its open pull request. No bundle
/// is produced — the submit step locates the PR by <see cref="Branch"/> and closes it with the
/// write credential.</summary>
internal record QueuedTaskRevert(RixBranchName Branch);

/// <summary>The deliverable form of a <see cref="QueuedTaskRevert"/>, carried in <c>result.json</c>
/// so <c>rix submit</c> can apply it. Identical shape to the queued form — kept as a separate type
/// to mirror the queued/pending split of PRs and pushes.</summary>
internal record PendingTaskRevert
(
    [property: JsonPropertyName("branch")] RixBranchName Branch
);
