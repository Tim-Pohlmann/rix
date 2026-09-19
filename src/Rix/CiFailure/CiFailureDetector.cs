using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>
/// Given a specific workflow run, verifies it actually failed and, if so, builds a prompt
/// describing the failure (PR number, run URL, failing step logs) for a coding agent to act on.
/// Replaces what used to be bash + <c>gh</c> CLI in <c>on-ci-failure.yml</c>, so the "turn a
/// failure into a prompt" logic lives in one tested place instead of a workflow script. Also the
/// one place that decides a failure is not worth answering at all — a run that didn't fail, one
/// rix isn't allowed to answer because it came from a fork, or one whose branch rix has already
/// been fixing on its own for too long. Deciding what to do with the
/// outcome is <see cref="CiFailureRunner"/>'s job, not this one's.
/// </summary>
internal static class CiFailureDetector
{
    /// <summary>Caps the log excerpt so a flooding failure can't blow the model's context budget.
    /// The host applies it while streaming the logs, so it bounds what is held in memory as well as
    /// what ends up in the prompt, and it covers the excerpt as a whole rather than each failed job
    /// — nothing here re-trims what comes back.</summary>
    private const int LogTailChars = 20_000;

    internal static async Task<ICiFailureResult> DetectAsync
    (
        RepoIdentifier repo,
        RunId runId,
        ICiHost ci,
        ICiFailureRepoHost repoHost,
        MaxRixCommits maxRixCommits,
        CancellationToken cancellationToken
    )
    {
        // Both hosts fail the same way — an exception whose message already names the operation
        // that failed — so one catch at the boundary replaces a try/catch per call, and a
        // CiFailureError carries that message through unchanged. Two types rather than one because
        // the CI provider and the repo host are separately chosen and can fail separately; nothing
        // here branches on which, so they are caught together.
        try
        {
            var run = await ci.GetRunAsync(runId, cancellationToken);
            if (run.Conclusion != "failure")
                return new CiFailureSkipped(run.Conclusion);

            // The trust boundary, applied before a single byte of the run reaches a prompt: getting
            // a branch into this repo takes write access to it, so a run whose head is this repo was
            // put there by someone who has it, and a run whose head is a fork was not. Compared
            // case-insensitively because GitHub treats owner and repo names that way, so the same
            // repo can be named in either case and still be the same repo.
            if (!run.HeadRepo.Equals(repo.Value, StringComparison.OrdinalIgnoreCase))
                return new CiFailureUntrustedRun(run.HeadRepo, run.HeadBranch);

            // Answered before anything else is fetched, rather than concurrently with it: it is the
            // one question whose answer makes all the remaining work pointless, and the log fetch is
            // by far the most expensive call here. A single extra round-trip ahead of a run that then
            // spends minutes on a coding agent is the cheaper half of that trade.
            var branch = new BranchName(run.HeadBranch);
            var rixCommits = await repoHost.CountLeadingRixCommitsAsync(branch, maxRixCommits, cancellationToken);
            if (rixCommits >= maxRixCommits.Value)
                return new CiFailureLoopGuarded(run.HeadBranch, rixCommits);

            // Independent of each other - only the already-fetched run is needed by both - so they
            // run concurrently rather than paying two sequential network round-trips.
            var logsTask = ci.GetFailedJobLogsAsync(runId, LogTailChars, cancellationToken);
            var prTask = repoHost.FindOpenPullRequestNumberAsync(branch, cancellationToken);
            await Task.WhenAll(logsTask, prTask);
            var logs = logsTask.Result;
            var prNumber = prTask.Result;
            var prompt = BuildPrompt(repo, run, prNumber, logs);
            return new CiFailureDetected(prompt, run.HtmlUrl, run.HeadBranch, prNumber);
        }
        catch (Exception ex) when (ex is CiHostException or RepoHostException)
        {
            return new CiFailureError(ex.Message);
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
