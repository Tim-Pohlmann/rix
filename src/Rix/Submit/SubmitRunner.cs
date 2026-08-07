using Rix.Job;
using Rix.Process;
using System.Text.Json;

namespace Rix.Submit;

/// <summary>
/// Turns the read-only output of <c>rix job</c> (a <c>result.json</c> plus one git bundle per
/// proposed change) into real remote state: clone the target with a write credential, then for
/// each pending PR fetch its bundle, push the branch, and open the PR, for each pending push
/// fetch its bundle and push the commits onto the branch it already exists on, and for each
/// pending task update/revert patch or close the already-submitted task's pull request. Fails
/// fast if a PR's branch already exists on the remote so an in-flight or previously merged branch
/// is never silently overwritten; a push onto an existing branch is left to git's own
/// non-fast-forward guard to protect.
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
        var pendingUpdates = success.PendingUpdateRequests ?? [];
        var pendingReverts = success.PendingRevertRequests ?? [];

        if (success.PendingPrRequests.Count == 0
            && pendingPushes.Count == 0
            && pendingUpdates.Count == 0
            && pendingReverts.Count == 0)
            return new SubmitSuccess([], [], [], []);

        using var cloneDir = TempDirectory.Create(config.WorkDir.Value, "rix-submit");

        // Only PRs and pushes need a clone: their bundles are fetched into it and the branches
        // pushed from there. Updates and reverts patch or close an already-submitted task's PR
        // straight through the API, so a pure update/revert run must not clone the repo (nor
        // require its write token to be clone-capable).
        if (success.PendingPrRequests.Count > 0 || pendingPushes.Count > 0)
            await context.Host.CloneAsync(cloneDir.Path, cancellationToken);

        var created = new List<CreatedPr>();
        var pushed = new List<string>();
        var updated = new List<UpdatedPr>();
        var closed = new List<ClosedPr>();
        foreach (var pr in success.PendingPrRequests)
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
        foreach (var update in pendingUpdates)
        {
            switch (await SubmitUpdateAsync(context, update, cancellationToken))
            {
                case SubmitOneFailed(var failure):
                    return failure;
                case SubmitOneUpdated(var branch, var url):
                    updated.Add(new UpdatedPr(branch, url));
                    break;
                default:
                    throw new NotSupportedException($"Unexpected submit outcome for {update.Branch.Value}");
            }
        }
        foreach (var revert in pendingReverts)
        {
            switch (await SubmitRevertAsync(context, revert, cancellationToken))
            {
                case SubmitOneFailed(var failure):
                    return failure;
                case SubmitOneClosed(var branch, var url):
                    closed.Add(new ClosedPr(branch, url));
                    break;
                default:
                    throw new NotSupportedException($"Unexpected submit outcome for {revert.Branch.Value}");
            }
        }

        return new SubmitSuccess(created, pushed, updated, closed);
    }

    /// <summary>Fetches one PR's bundle, pushes its branch, and opens the PR. Returns the opened
    /// PR's URL on success, or a <see cref="SubmitFailure"/> (nested in <see cref="SubmitOneFailed"/>)
    /// on the first problem, which aborts the whole run.</summary>
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

        string url;
        try
        {
            url = await context.Host.CreatePullRequestAsync(pr, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new SubmitOneFailed(new SubmitFailure($"creating PR for {pr.Branch.Value} failed: {ex.Message}"));
        }

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

    /// <summary>Patches the already-submitted task's pull request — its title and/or body — via the
    /// write host, which locates the open PR by the task's branch.</summary>
    private static async Task<SubmitOneOutcome> SubmitUpdateAsync
    (
        SubmitContext context,
        PendingTaskUpdate update,
        CancellationToken cancellationToken
    )
    {
        string url;
        try
        {
            url = await context.Host.UpdatePullRequestAsync(update, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return new SubmitOneFailed(new SubmitFailure($"updating task {update.Branch.Value} failed: {ex.Message}"));
        }

        context.LogLine($"updated PR for {update.Branch.Value}");
        return new SubmitOneUpdated(update.Branch.Value, url);
    }

    /// <summary>Closes the already-submitted task's pull request via the write host, which locates
    /// the open PR by the task's branch.</summary>
    private static async Task<SubmitOneOutcome> SubmitRevertAsync
    (
        SubmitContext context,
        PendingTaskRevert revert,
        CancellationToken cancellationToken
    )
    {
        string url;
        try
        {
            url = await context.Host.ClosePullRequestAsync(revert, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            return new SubmitOneFailed(new SubmitFailure($"reverting task {revert.Branch.Value} failed: {ex.Message}"));
        }

        context.LogLine($"closed PR for {revert.Branch.Value}");
        return new SubmitOneClosed(revert.Branch.Value, url);
    }

    /// <summary>Unbundles the PR's branch from its local bundle and pushes it to the remote.
    /// Returns a <see cref="SubmitFailure"/> on the first problem, or <c>null</c> on success.</summary>
    private static async Task<SubmitFailure?> DeliverBranchAsync
    (
        SubmitContext context, string cloneDir, string bundlePath, RixBranchName branch, CancellationToken cancellationToken
    )
    {
        var fetch = await Git
        (
            context, cloneDir, ["fetch", bundlePath, $"{branch.Value}:{branch.Value}"], cancellationToken
        );
        if (fetch is ProcessFailure fetchFailure)
            return new SubmitFailure($"git fetch failed for {branch.Value}: {fetchFailure.Reason}");

        try
        {
            await context.Host.PushBranchAsync(cloneDir, branch, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new SubmitFailure($"git push failed for {branch.Value}: {ex.Message}");
        }

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
    private sealed record SubmitOneUpdated(string Branch, string Url) : SubmitOneOutcome;
    private sealed record SubmitOneClosed(string Branch, string Url) : SubmitOneOutcome;
    private sealed record SubmitOneFailed(SubmitFailure Failure) : SubmitOneOutcome;
}
