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
///
/// Treats <c>result.json</c> as untrusted input rather than as its own earlier output: the agent
/// that produced it runs unsandboxed in the same workspace and can rewrite it before this command
/// ever opens it. Everything it names is therefore re-checked here, where the write credential
/// actually is — see <see cref="SubmitPushAsync"/> for the branch the check turns on.
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
        catch (RepoHostException ex)
        {
            return new SubmitFailure(ex.Message);
        }
    }

    /// <summary>Clones the target, then delivers every pending PR and push in dependency order.
    /// Non-host problems (missing bundle file, a failed local <c>git fetch</c>) short-circuit as a
    /// returned <see cref="SubmitFailure"/>; host failures throw <see cref="RepoHostException"/>
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

        await context.RepoHost.CloneAsync(cloneDir.Path, cancellationToken);

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
    /// <see cref="RepoHostException"/> past this method to <see cref="RunAsync"/>'s catch.</summary>
    private static async Task<SubmitOneOutcome> SubmitPrAsync
    (
        SubmitConfig config,
        SubmitContext context,
        string cloneDir,
        PendingPr pr,
        CancellationToken cancellationToken
    )
    {
        if (await context.RepoHost.BranchExistsOnRemoteAsync(pr.Branch, cancellationToken))
            return new SubmitOneFailed(new SubmitFailure($"branch already exists on remote: {pr.Branch.Value}"));

        if (await DeliverBranchAsync(config, context, cloneDir, pr.Branch, pr.BundleFile, cancellationToken) is { } deliverFailure)
            return new SubmitOneFailed(deliverFailure);

        var url = await context.RepoHost.CreatePullRequestAsync(pr, cancellationToken);

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
        // Checked again here even though rix job's /push endpoint already refused anything outside
        // this list. That check bound only what the agent asked for: the agent is an ordinary
        // process on the same runner as the result.json carrying its answer here, so it can name
        // any branch it likes in the file afterwards. This is the copy of the check that stands
        // next to the write credential, and so the one that decides what is actually pushed. The
        // rix/* rule a pending PR must satisfy needs no such repetition — RixBranchName re-applies
        // it when result.json is deserialized, because it rides the type rather than the config.
        if (!config.AllowedPushBranches.Contains(push.Branch))
            return new SubmitOneFailed(new SubmitFailure($"branch is not allowed to be pushed to: {push.Branch.Value}"));

        if (await DeliverBranchAsync(config, context, cloneDir, push.Branch, push.BundleFile, cancellationToken) is { } deliverFailure)
            return new SubmitOneFailed(deliverFailure);

        context.LogLine($"pushed commits to {push.Branch.Value}");
        return new SubmitOnePushed(push.Branch.Value);
    }

    /// <summary>Unbundles <paramref name="branch"/> from <paramref name="bundleFile"/> in the input
    /// dir and pushes it to the remote - shared by both PR and push delivery (see the two callers
    /// above). Returns a <see cref="SubmitFailure"/> if the bundle is missing or the local
    /// <c>git fetch</c> fails, or <c>null</c> once the branch is pushed; a push failure throws
    /// <see cref="RepoHostException"/> instead.</summary>
    private static async Task<SubmitFailure?> DeliverBranchAsync
    (
        SubmitConfig config,
        SubmitContext context,
        string cloneDir,
        BranchName branch,
        string bundleFile,
        CancellationToken cancellationToken
    )
    {
        var bundlePath = Path.Combine(config.InputDir.Value, bundleFile);
        if (!File.Exists(bundlePath))
            return new SubmitFailure($"bundle file not found: {bundleFile}");

        // --end-of-options stops git from reading a branch name starting with "-" as an option —
        // see GitHubJobRepoHost.CreateBundleAsync for why it's this flag and not "--".
        var fetch = await Git
        (
            context, cloneDir, ["fetch", bundlePath, "--end-of-options", $"{branch.Value}:{branch.Value}"], cancellationToken
        );
        if (fetch is ProcessFailure fetchFailure)
            return new SubmitFailure($"git fetch failed for {branch.Value}: {fetchFailure.Reason}");

        await context.RepoHost.PushBranchAsync(cloneDir, branch, cancellationToken);
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
