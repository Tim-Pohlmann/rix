using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>
/// Given a specific workflow run, verifies it actually failed and, if so, builds a prompt
/// describing the failure (PR number, run URL, failing step logs) for a coding agent to act on.
/// Replaces what used to be bash + <c>gh</c> CLI in <c>on-ci-failure.yml</c>, so the "turn a
/// failure into a prompt" logic lives in one tested place instead of a workflow script.
/// </summary>
internal static class CiFailureRunner
{
    /// <summary>Caps the log excerpt so a flooding failure can't blow the model's context budget.</summary>
    private const int LogTailChars = 20_000;

    internal static async Task<ICiFailureResult> RunAsync(CiFailureConfig config, IGitHubCiFailureHost host, CancellationToken cancellationToken)
    {
        WorkflowRun run;
        try
        {
            run = await FetchAsync(ct => host.GetRunAsync(config.RunId, ct), $"could not fetch run {config.RunId}", cancellationToken);
        }
        catch (CiFailureFetchException ex)
        {
            return new CiFailureError(ex.Message);
        }

        if (run.Conclusion != "failure")
            return new CiFailureSkipped(run.Conclusion);

        // Independent of each other - only the already-fetched run is needed by both - so they run
        // concurrently rather than paying two sequential network round-trips.
        var logsTask = FetchAsync(ct => host.GetFailedJobLogsAsync(config.RunId, ct), $"could not fetch failing job logs for run {config.RunId}", cancellationToken);
        var prTask = FetchAsync(ct => host.FindOpenPullRequestNumberAsync(new BranchName(run.HeadBranch), ct), $"could not look up open PR for branch {run.HeadBranch}", cancellationToken);

        try
        {
            await Task.WhenAll(logsTask, prTask);
        }
        catch (CiFailureFetchException ex)
        {
            return new CiFailureError(ex.Message);
        }
        var logs = logsTask.Result;
        var prNumber = prTask.Result;

        if (logs.Length > LogTailChars)
            logs = logs[^LogTailChars..];

        var prompt = BuildPrompt(config.Repo, run, prNumber, logs);
        return new CiFailureDetected(prompt, run.HtmlUrl, run.HeadBranch, prNumber);
    }

    /// <summary>Runs <paramref name="call"/> with <paramref name="cancellationToken"/> forwarded,
    /// wrapping any <see cref="HttpRequestException"/> as a <see cref="CiFailureFetchException"/>
    /// carrying a message prefixed with <paramref name="what"/> — collapses what would otherwise be
    /// a separate try/catch per API call into one shared helper, while keeping each call's own
    /// failure message.</summary>
    private static async Task<T> FetchAsync<T>(Func<CancellationToken, Task<T>> call, string what, CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new CiFailureFetchException($"{what}: {ex.Message}");
        }
    }

    private sealed class CiFailureFetchException(string message) : Exception(message);

    private static string BuildPrompt(RepoIdentifier repo, WorkflowRun run, int? prNumber, string logs)
    {
        var prLine = prNumber switch
        {
            { } number => $"This is PR #{number} in {repo.Value}.\n",
            null => "",
        };

        return $"""
        CI failed on branch '{run.HeadBranch}' (run: {run.HtmlUrl}).
        {prLine}Failing run title: {run.DisplayTitle}

        Investigate the failure and fix it. Failing step log (tail):
        ```
        {logs}
        ```
        """;
    }
}
