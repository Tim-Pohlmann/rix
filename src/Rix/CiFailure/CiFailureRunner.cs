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
            run = await host.GetRunAsync(config.RunId, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new CiFailureError($"could not fetch run {config.RunId}: {ex.Message}");
        }

        if (run.Conclusion != "failure")
            return new CiFailureSkipped(run.Conclusion);

        string logs;
        try
        {
            logs = await host.GetFailedJobLogsAsync(config.RunId, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new CiFailureError($"could not fetch failing job logs for run {config.RunId}: {ex.Message}");
        }
        if (logs.Length > LogTailChars)
            logs = logs[^LogTailChars..];

        int? prNumber;
        try
        {
            prNumber = await host.FindOpenPullRequestNumberAsync(new BranchName(run.HeadBranch), cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new CiFailureError($"could not look up open PR for branch {run.HeadBranch}: {ex.Message}");
        }

        var prompt = BuildPrompt(config.Repo, run, prNumber, logs);
        return new CiFailureDetected(prompt, run.HtmlUrl, run.HeadBranch, prNumber);
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
