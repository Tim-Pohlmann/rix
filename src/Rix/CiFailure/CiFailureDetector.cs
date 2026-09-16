using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>
/// Given a specific workflow run, verifies it actually failed and, if so, builds a prompt
/// describing the failure (PR number, run URL, failing step logs) for a coding agent to act on.
/// Replaces what used to be bash + <c>gh</c> CLI in <c>on-ci-failure.yml</c>, so the "turn a
/// failure into a prompt" logic lives in one tested place instead of a workflow script. Deciding
/// what to do with the outcome is <see cref="CiFailureRunner"/>'s job, not this one's.
/// </summary>
internal static class CiFailureDetector
{
    /// <summary>Caps the log excerpt so a flooding failure can't blow the model's context budget.</summary>
    private const int LogTailChars = 20_000;

    internal static async Task<ICiFailureResult> DetectAsync(RepoIdentifier repo, RunId runId, IGitHubCiFailureHost host, CancellationToken cancellationToken)
    {
        WorkflowRun run;
        try
        {
            run = await FetchAsync(ct => host.GetRunAsync(runId, ct), $"could not fetch run {runId.Value}", cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new CiFailureError(ex.Message);
        }

        if (run.Conclusion != "failure")
            return new CiFailureSkipped(run.Conclusion);

        // Independent of each other - only the already-fetched run is needed by both - so they run
        // concurrently rather than paying two sequential network round-trips.
        var logsTask = FetchAsync(ct => host.GetFailedJobLogsAsync(runId, ct), $"could not fetch failing job logs for run {runId.Value}", cancellationToken);
        var prTask = FetchAsync(ct => host.FindOpenPullRequestNumberAsync(new BranchName(run.HeadBranch), ct), $"could not look up open PR for branch {run.HeadBranch}", cancellationToken);

        try
        {
            await Task.WhenAll(logsTask, prTask);
        }
        catch (HttpRequestException ex)
        {
            return new CiFailureError(ex.Message);
        }
        var logs = logsTask.Result;
        var prNumber = prTask.Result;

        if (logs.Length > LogTailChars)
            logs = logs[^LogTailChars..];

        var prompt = BuildPrompt(repo, run, prNumber, logs);
        return new CiFailureDetected(prompt, run.HtmlUrl, run.HeadBranch, prNumber);
    }

    /// <summary>Runs <paramref name="call"/> with <paramref name="cancellationToken"/> forwarded,
    /// rethrowing any <see cref="HttpRequestException"/> with its message prefixed by
    /// <paramref name="what"/> — collapses what would otherwise be a separate try/catch per API
    /// call into one shared helper, while keeping each call's own failure message. Every HTTP call
    /// this class makes goes through here, so catching that type at the call sites catches exactly
    /// the failures described this way.</summary>
    private static async Task<T> FetchAsync<T>(Func<CancellationToken, Task<T>> call, string what, CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException($"{what}: {ex.Message}", ex);
        }
    }

    private static string BuildPrompt(RepoIdentifier repo, WorkflowRun run, int? prNumber, string logs)
    {
        var prLine = prNumber switch
        {
            { } number => $"This is PR #{number} in {repo.Value}.",
            null => "",
        };

        return $"""
        CI failed on branch '{run.HeadBranch}' (run: {run.HtmlUrl}).
        {prLine}
        Failing run title: {run.DisplayTitle}

        Investigate the failure and fix it. Failing step log (tail):
        ```
        {logs}
        ```
        """;
    }
}
