using Rix.Job;
using Rix.Process;
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

        var orderedPrs = PrDependencyOrder.TryOrder(success.PendingPrRequests, pr => pr.Branch.Value, pr => pr.BaseBranch.Value);
        if (orderedPrs is null)
            return new SubmitFailure("queued PRs have a cyclic base-branch dependency");

        using var cloneDir = TempDirectory.Create(config.WorkDir.Value, "rix-submit");

        await context.Host.CloneAsync(cloneDir.Path, cancellationToken);

        var created = new List<CreatedPr>();
        var pushed = new List<string>();
        foreach (var pr in orderedPrs)
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
    private sealed record SubmitOneFailed(SubmitFailure Failure) : SubmitOneOutcome;
}
