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
        // Every host call here fails the same way — a CiFailureError naming the operation that
        // threw — so one catch at the boundary replaces a per-call try/catch. The host's exception
        // message already says which fetch failed.
        try
        {
            var run = await host.GetRunAsync(config.RunId, cancellationToken);

            if (run.Conclusion != "failure")
                return new CiFailureSkipped(run.Conclusion);

            // Independent of each other - only the already-fetched run is needed by both - so they
            // run concurrently rather than paying two sequential network round-trips.
            var logsTask = host.GetFailedJobLogsAsync(config.RunId, cancellationToken);
            var prTask = host.FindOpenPullRequestNumberAsync(new BranchName(run.HeadBranch), cancellationToken);
            await Task.WhenAll(logsTask, prTask);

            var logs = await logsTask;
            var prNumber = await prTask;

            if (logs.Length > LogTailChars)
                logs = logs[^LogTailChars..];

            var prompt = BuildPrompt(config.Repo, run, prNumber, logs);
            return new CiFailureDetected(prompt, run.HtmlUrl, run.HeadBranch, prNumber);
        }
        catch (RepositoryHostException ex)
        {
            return new CiFailureError(ex.Message);
        }
    }

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
