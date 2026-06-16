using System.Text.Json;
using Rix.Job;
using Rix.Process;

namespace Rix.Submit;

/// <summary>
/// Turns the read-only output of <c>rix job</c> (a <c>result.json</c> plus one git bundle per
/// proposed PR) into real pull requests: clone the target with a write credential, then for each
/// pending PR fetch its bundle, push the branch, and open the PR. Fails fast if a branch already
/// exists on the remote so an in-flight or previously merged branch is never silently overwritten.
/// </summary>
internal static class SubmitRunner
{
    internal static async Task<ISubmitResult> RunAsync(
        SubmitConfig config,
        SubmitContext context,
        CancellationToken cancellationToken)
    {
        var resultPath = Path.Combine(config.InputDir.Value, "result.json");
        if (!File.Exists(resultPath))
            return new SubmitFailure($"result.json not found in {config.InputDir.Value}");

        IJobResult? jobResult;
        try
        {
            var json = await File.ReadAllTextAsync(resultPath, cancellationToken);
            jobResult = JsonSerializer.Deserialize(json, JobJsonContext.Default.IJobResult);
        }
        catch (JsonException ex)
        {
            return new SubmitFailure($"could not parse result.json: {ex.Message}");
        }

        if (jobResult is not JobSuccess success)
            return new SubmitFailure("result.json does not describe a successful job");

        if (success.PendingPrRequests.Count == 0)
            return new SubmitSuccess([]);

        var cloneDir = Path.Combine(config.WorkDir.Value, $"rix-submit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cloneDir);
        try
        {
            await context.Host.CloneAsync(cloneDir, cancellationToken);

            var created = new List<string>();
            foreach (var pr in success.PendingPrRequests)
            {
                if (await context.Host.BranchExistsOnRemoteAsync(pr.Branch, cancellationToken))
                    return new SubmitFailure($"branch already exists on remote: {pr.Branch.Value}");

                var bundlePath = Path.Combine(config.InputDir.Value, pr.BundleFile);
                if (!File.Exists(bundlePath))
                    return new SubmitFailure($"bundle file not found: {pr.BundleFile}");

                var fetch = await Git(
                    context, cloneDir, ["fetch", bundlePath, $"{pr.Branch.Value}:{pr.Branch.Value}"], cancellationToken);
                if (fetch is ProcessFailure fetchFailure)
                    return new SubmitFailure($"git fetch failed for {pr.Branch.Value}: {fetchFailure.Reason}");

                var push = await Git(
                    context, cloneDir, ["push", "origin", pr.Branch.Value], cancellationToken);
                if (push is ProcessFailure pushFailure)
                    return new SubmitFailure($"git push failed for {pr.Branch.Value}: {pushFailure.Reason}");

                try
                {
                    await context.Host.CreatePullRequestAsync(pr, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    return new SubmitFailure($"creating PR for {pr.Branch.Value} failed: {ex.Message}");
                }

                context.LogLine($"opened PR for {pr.Branch.Value}");
                created.Add(pr.Branch.Value);
            }

            return new SubmitSuccess(created);
        }
        finally
        {
            try { Directory.Delete(cloneDir, recursive: true); }
            catch (DirectoryNotFoundException) { /* already cleaned up */ }
        }
    }

    private static Task<ProcessResult> Git(
        SubmitContext context,
        string repoDir,
        IEnumerable<string> args,
        CancellationToken cancellationToken) =>
        context.RunProcess("git", ["-C", repoDir, .. args], repoDir, null, null, cancellationToken);
}
