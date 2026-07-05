using Rix.Job;
using Rix.Process;
using System.Text.Json;

namespace Rix.Submit;

/// <summary>
/// Turns the read-only output of <c>rix job</c> (a <c>result.json</c> plus one git bundle per
/// proposed PR) into real pull requests: clone the target with a write credential, then for each
/// pending PR fetch its bundle, push the branch, and open the PR. Fails fast if a branch already
/// exists on the remote so an in-flight or previously merged branch is never silently overwritten.
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

        if (success.PendingPrRequests.Count == 0)
            return new SubmitSuccess([]);

        using var cloneDir = TempDirectory.Create(config.WorkDir.Value, "rix-submit");

        await context.Host.CloneAsync(cloneDir.Path, cancellationToken);

        var created = new List<string>();
        foreach (var pr in success.PendingPrRequests)
        {
            if (await SubmitOneAsync(config, context, cloneDir.Path, pr, cancellationToken) is { } failure)
                return failure;
            created.Add(pr.Branch.Value);
        }

        return new SubmitSuccess(created);
    }

    /// <summary>Fetches one PR's bundle, pushes its branch, and opens the PR. Returns a
    /// <see cref="SubmitFailure"/> on the first problem, or <c>null</c> on success.</summary>
    private static async Task<SubmitFailure?> SubmitOneAsync
    (
        SubmitConfig config,
        SubmitContext context,
        string cloneDir,
        PendingPr pr,
        CancellationToken cancellationToken
    )
    {
        if (await context.Host.BranchExistsOnRemoteAsync(pr.Branch, cancellationToken))
            return new SubmitFailure($"branch already exists on remote: {pr.Branch.Value}");

        var bundlePath = Path.Combine(config.InputDir.Value, pr.BundleFile);
        if (!File.Exists(bundlePath))
            return new SubmitFailure($"bundle file not found: {pr.BundleFile}");

        if (await DeliverBranchAsync(context, cloneDir, bundlePath, pr.Branch, cancellationToken) is { } deliverFailure)
            return deliverFailure;

        try
        {
            await context.Host.CreatePullRequestAsync(pr, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new SubmitFailure($"creating PR for {pr.Branch.Value} failed: {ex.Message}");
        }

        context.LogLine($"opened PR for {pr.Branch.Value}");
        return null;
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
}
