using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>
/// Given a specific workflow run, verifies it actually failed and, if so, builds a prompt
/// describing the failure (PR number, run URL, failing step logs) for a coding agent to act on.
/// Replaces what used to be bash + <c>gh</c> CLI in <c>on-ci-failure.yml</c>, so the "turn a
/// failure into a prompt" logic lives in one tested place instead of a workflow script. Also the
/// one place that decides a failure is not worth answering at all — a run that didn't fail, one
/// rix isn't allowed to answer because it came from a fork, or one whose branch rix has already
/// been fixing on its own for too long. Deciding what to do with the outcome is the caller's job,
/// not this one's: reaching a verdict is the whole of <c>rix ci-failure</c>, and answering the
/// failure is a separate <c>rix job</c> run on a machine this one never touches.
///
/// Named for the command it is the whole of, like <see cref="Rix.Job.JobRunner"/> and
/// <see cref="Rix.Submit.SubmitRunner"/>, rather than for the one thing it does.
/// </summary>
internal static class CiFailureRunner
{
    /// <summary>Caps the log excerpt so a flooding failure can't blow the model's context budget.
    /// The host applies it while streaming the logs, so it bounds what is held in memory as well as
    /// what ends up in the prompt, and it covers the excerpt as a whole rather than each failed job
    /// — nothing here re-trims what comes back.</summary>
    private const int LogTailChars = 20_000;

    /// <summary>Checks a run and leaves its output in <see cref="CiFailureConfig.OutputDir"/>. The
    /// writes happen here rather than in the caller because <c>result.json</c> and <c>prompt.md</c>
    /// are the check's product (see <see cref="CiFailureArtifacts"/>), and because one of them can
    /// still change the verdict: a prompt that never reached disk is reported as a
    /// <see cref="CiFailureError"/>, which only works while the write is inside the thing that
    /// returns the verdict. What the caller adds is a CLI's own: printing the verdict and choosing
    /// an exit code.</summary>
    internal static async Task<ICiFailureResult> RunAsync
    (
        CiFailureConfig config,
        CiFailureContext context,
        CancellationToken cancellationToken
    )
    {
        var result = await CheckAsync(config, context, cancellationToken);
        return await CiFailureArtifacts.WriteAsync(config, context, result);
    }

    /// <summary>The check itself, in one method so each way it can end is a plain <c>return</c> and
    /// the artifacts are written once, by <see cref="RunAsync"/>, whichever one it took.</summary>
    private static async Task<ICiFailureResult> CheckAsync
    (
        CiFailureConfig config,
        CiFailureContext context,
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
            var run = await context.Ci.GetRunAsync(config.RunId, cancellationToken);
            if (run.Outcome is not CiFailed)
                return new CiFailureSkipped(run.Outcome.Name);

            // The trust boundary, applied before a single byte of the run reaches a prompt: getting
            // a branch into this repo takes write access to it, so a run whose head is this repo was
            // put there by someone who has it, and a run whose head is a fork was not. What makes
            // two repo identities the same one is RepoIdentifier's own rule, not this comparison's.
            if (run.HeadRepo != config.Repo)
                return new CiFailureUntrustedRun(run.HeadRepo.Value, run.HeadBranch.Value);

            // Answered before anything else is fetched, rather than concurrently with it: it is the
            // one question whose answer makes all the remaining work pointless, and the log fetch is
            // by far the most expensive call here. A single extra round-trip ahead of a run that then
            // spends minutes on a coding agent is the cheaper half of that trade.
            var rixCommits = await context.RepoHost.CountLeadingRixCommitsAsync(run.HeadBranch, config.MaxRixCommits, cancellationToken);
            if (rixCommits >= config.MaxRixCommits.Value)
                return new CiFailureLoopGuarded(run.HeadBranch.Value, rixCommits);

            // Independent of each other - only the already-fetched run is needed by both - so they
            // run concurrently rather than paying two sequential network round-trips.
            var logsTask = context.Ci.GetFailedJobLogsAsync(config.RunId, LogTailChars, cancellationToken);
            var prTask = context.RepoHost.FindOpenPullRequestNumberAsync(run.HeadBranch, cancellationToken);
            await Task.WhenAll(logsTask, prTask);
            var logs = logsTask.Result;
            var prNumber = prTask.Result;
            var prompt = BuildPrompt(config.Repo, run, prNumber, logs);
            return new CiFailureDetected
            (
                prompt, run.Url, run.HeadBranch.Value, prNumber, CiFailureArtifacts.PromptFile(config.OutputDir)
            );
        }
        catch (Exception ex) when (ex is CiHostException or RepoHostException)
        {
            return new CiFailureError(ex.Message);
        }
    }

    private static string BuildPrompt(RepoIdentifier repo, CiRun run, int? prNumber, string logs)
    {
        var prLine = prNumber switch
        {
            { } number => $"This is PR #{number} in {repo.Value}.",
            null => "",
        };

        return $"""
        CI failed on branch '{run.HeadBranch.Value}' (run: {run.Url}).
        {prLine}
        Failing run title: {run.Title}

        Investigate the failure and fix it. Failing step log (tail):
        ```
        {logs}
        ```
        """;
    }
}
