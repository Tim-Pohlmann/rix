using Rix.Job;
using Rix.Process;
using Rix.Repository;
using System.Text.Json;

namespace Rix.Submit;

/// <summary>
/// Turns the read-only output of <c>rix job</c> (a <c>result.json</c> plus one git bundle per
/// proposed change) into real remote state: clone the target with a write credential, then for
/// each pending PR fetch its bundle, push the branch, and open the PR, and for each pending push
/// fetch its bundle and push the commits onto the branch it already exists on. Fails fast if a
/// PR's branch already exists on the remote so an in-flight or previously merged branch is never
/// silently overwritten; a push onto an existing branch is left to git's own non-fast-forward
/// guard to protect.
/// </summary>
internal static class SubmitRunner
{
    internal static async Task<ISubmitResult> RunAsync
    (
        SubmitConfig config,
        SubmitContext context,
        CancellationToken cancellationToken
    )
    {
        var resultPath = Path.Combine(config.InputDir.Value, "result.json");
        if (!File.Exists(resultPath))
            return new SubmitFailure($"result.json not found in {config.InputDir.Value}");

        IJobResult? jobResult;
        try
        {
            await using var stream = File.OpenRead(resultPath);
            jobResult = await JsonSerializer.DeserializeAsync
            (
                stream, JobJsonContext.Default.IJobResult, cancellationToken
            );
        }
        catch (JsonException ex)
        {
            return new SubmitFailure($"could not parse result.json: {ex.Message}");
        }

        if (jobResult is not JobSuccess success)
            return new SubmitFailure("result.json does not describe a successful job");

        var pendingPushes = success.PendingPushRequests ?? [];

        if (success.PendingPrRequests.Count == 0 && pendingPushes.Count == 0)
            return new SubmitSuccess([], []);

        // Every host failure (clone, remote branch check, push, open PR) is terminal for the whole
        // run and maps to the same SubmitFailure, so DeliverAllAsync lets them throw and they're
        // caught once here rather than at each call — the message thrown already names the operation.
        try
        {
            return await DeliverAllAsync(config, context, success.PendingPrRequests, pendingPushes, cancellationToken);
        }
        catch (RepositoryHostException ex)
        {
            return new SubmitFailure(ex.Message);
        }
    }

    /// <summary>Clones the target, then delivers every pending PR and push in dependency order.
    /// Non-host problems (missing bundle file, a failed local <c>git fetch</c>) short-circuit as a
    /// returned <see cref="SubmitFailure"/>; host failures throw <see cref="RepositoryHostException"/>
    /// straight through to the single catch in <see cref="RunAsync"/>.</summary>
    private static async Task<ISubmitResult> DeliverAllAsync
    (
        SubmitConfig config,
        SubmitContext context,
        IReadOnlyList<PendingPr> pendingPrs,
        IReadOnlyList<PendingPush> pendingPushes,
        CancellationToken cancellationToken
    )
    {
        using var cloneDir = TempDirectory.Create(config.WorkDir.Value, "rix-submit");

        await context.Host.CloneAsync(cloneDir.Path, cancellationToken);

        var created = new List<CreatedPr>();
        var pushed = new List<string>();
        // result.json's PendingPrRequests is written from LocalApiServer's PrQueue, which only ever
        // accepts a PR if it keeps the whole queue in a valid base-branch dependency order — so this
        // is already base-first, no reordering needed here.
        foreach (var pr in pendingPrs)
        {
            switch (await SubmitPrAsync(config, context, cloneDir.Path, pr, cancellationToken))
            {
                case SubmitOneFailed(var failure):
                    return failure;
                case SubmitOneSucceeded(var url):
                    created.Add(new CreatedPr(pr.Branch.Value, url));
                    break;
                default:
                    throw new NotSupportedException($"Unexpected submit outcome for {pr.Branch.Value}");
            }
        }
        foreach (var push in pendingPushes)
        {
            switch (await SubmitPushAsync(config, context, cloneDir.Path, push, cancellationToken))
            {
                case SubmitOneFailed(var failure):
                    return failure;
                case SubmitOnePushed(var branch):
                    pushed.Add(branch);
                    break;
                default:
                    throw new NotSupportedException($"Unexpected submit outcome for {push.Branch.Value}");
            }
        }

        return new SubmitSuccess(created, pushed);
    }

    /// <summary>Fetches one PR's bundle, pushes its branch, and opens the PR. Returns the opened
    /// PR's URL on success, or a <see cref="SubmitFailure"/> (nested in <see cref="SubmitOneFailed"/>)
    /// for a non-host problem — branch already on the remote, missing bundle, failed local fetch.
    /// A host failure (the remote-branch check or opening the PR) instead throws
    /// <see cref="RepositoryHostException"/> past this method to <see cref="RunAsync"/>'s catch.</summary>
    private static async Task<SubmitOneOutcome> SubmitPrAsync
    (
        SubmitConfig config,
        SubmitContext context,
        string cloneDir,
        PendingPr pr,
        CancellationToken cancellationToken
    )
    {
        if (await context.Host.BranchExistsOnRemoteAsync(pr.Branch, cancellationToken))
            return new SubmitOneFailed(new SubmitFailure($"branch already exists on remote: {pr.Branch.Value}"));

        var bundlePath = Path.Combine(config.InputDir.Value, pr.BundleFile);
        if (!File.Exists(bundlePath))
            return new SubmitOneFailed(new SubmitFailure($"bundle file not found: {pr.BundleFile}"));

        if (await DeliverBranchAsync(context, cloneDir, bundlePath, pr.Branch, cancellationToken) is { } deliverFailure)
            return new SubmitOneFailed(deliverFailure);

        var url = await context.Host.CreatePullRequestAsync(pr, cancellationToken);

        context.LogLine($"opened PR for {pr.Branch.Value}");
        return new SubmitOneSucceeded(url);
    }

    /// <summary>Fetches one push's bundle and pushes the commits onto its branch — which already
    /// exists on the remote by construction, so no PR is opened. Git's own fast-forward check
    /// protects a branch that advanced on the remote while the job ran; that surfaces here as a
    /// failed push rather than an overwrite.</summary>
    private static async Task<SubmitOneOutcome> SubmitPushAsync
    (
        SubmitConfig config,
        SubmitContext context,
        string cloneDir,
        PendingPush push,
        CancellationToken cancellationToken
    )
    {
        var bundlePath = Path.Combine(config.InputDir.Value, push.BundleFile);
        if (!File.Exists(bundlePath))
            return new SubmitOneFailed(new SubmitFailure($"bundle file not found: {push.BundleFile}"));

        if (await DeliverBranchAsync(context, cloneDir, bundlePath, push.Branch, cancellationToken) is { } deliverFailure)
            return new SubmitOneFailed(deliverFailure);

        context.LogLine($"pushed commits to {push.Branch.Value}");
        return new SubmitOnePushed(push.Branch.Value);
    }

    /// <summary>Unbundles <paramref name="branch"/> from its local bundle and pushes it to the
    /// remote - shared by both PR and push delivery (see the two callers above). Returns a
    /// <see cref="SubmitFailure"/> if the local <c>git fetch</c> fails, or <c>null</c> once the
    /// branch is pushed; a push failure throws <see cref="RepositoryHostException"/> instead.</summary>
    private static async Task<SubmitFailure?> DeliverBranchAsync
    (
        SubmitContext context, string cloneDir, string bundlePath, BranchName branch, CancellationToken cancellationToken
    )
    {
        var fetch = await Git
        (
            context, cloneDir, ["fetch", bundlePath, $"{branch.Value}:{branch.Value}"], cancellationToken
        );
        if (fetch is ProcessFailure fetchFailure)
            return new SubmitFailure($"git fetch failed for {branch.Value}: {fetchFailure.Reason}");

        await context.Host.PushBranchAsync(cloneDir, branch, cancellationToken);
        return null;
    }

    private static Task<ProcessResult> Git
    (
        SubmitContext context,
        string repoDir,
        IEnumerable<string> args,
        CancellationToken cancellationToken
    )
    => context.RunProcess("git", ["-C", repoDir, .. args], repoDir, null, null, cancellationToken);

    /// <summary>The result of submitting one pending PR: either the opened PR's URL, or a failure
    /// (nested so the caller keeps the typed <see cref="SubmitFailure"/> rather than re-deriving it).
    /// Modeled on <see cref="Rix.Job.JobRunner"/>'s delivery outcome, so the loop below pattern
    /// matches rather than distinguishing by nullability.</summary>
    private abstract record SubmitOneOutcome
    {
        private protected SubmitOneOutcome() { }
    }
    private sealed record SubmitOneSucceeded(string Url) : SubmitOneOutcome;
    private sealed record SubmitOnePushed(string Branch) : SubmitOneOutcome;
    private sealed record SubmitOneFailed(SubmitFailure Failure) : SubmitOneOutcome;
}
