namespace Rix.Repository;

/// <summary>Shapes the failing-job logs of one run into the single excerpt that goes in the agent's
/// prompt. Pure, and separate from the host that fetches those logs, because every rule here is
/// about what fits in a prompt rather than about talking to GitHub — and stating them away from the
/// HTTP calls is what lets them be exercised without one.</summary>
internal static class JobLogExcerpt
{
    /// <summary>How many failed jobs the excerpt covers at most. The caller's budget is split evenly
    /// across the jobs included, so every extra job shrinks all the others' share; past a handful
    /// each slice is too short to show a stack trace, which is worse than covering fewer jobs well.
    /// A matrix that fails in twenty configurations is almost always failing for one reason, so the
    /// jobs left out are named in the excerpt rather than covered.</summary>
    internal const int MaxJobs = 5;

    /// <summary>Each included job's share of <paramref name="totalTailChars"/>. At least one
    /// character each, so a budget smaller than the job count still yields a log rather than a
    /// division that truncates to nothing.</summary>
    internal static int TailCharsPerJob(int totalTailChars, int jobCount)
    => Math.Max(totalTailChars / jobCount, 1);

    /// <summary>Lays each job's tail out under a heading naming it — without which a multi-job
    /// failure reads as one undifferentiated log whose parts can't be told apart — and names the
    /// count of any jobs past <see cref="MaxJobs"/> rather than silently dropping them.</summary>
    internal static string Render(IReadOnlyList<CiJob> included, IReadOnlyList<string> logs, int omittedJobs)
    {
        var excerpt = string.Join("\n", included.Select((job, index) => $"===== {job.Name} =====\n{logs[index]}"));
        return omittedJobs switch
        {
            0 => excerpt,
            var omitted => $"{excerpt}\n===== {omitted} further failed job(s) omitted =====",
        };
    }
}

/// <summary>One job of a CI run, reduced to what building an excerpt needs: the ID to fetch its log
/// by and a name to head its block with. The domain shape the excerpt is assembled in, as
/// <see cref="WorkflowRun"/> is for a run — which is why it holds a name GitHub may not have sent,
/// already resolved, where <see cref="WorkflowJobApiResponse"/> holds the nullable wire field.
/// Whether a job failed is not part of it: that is the question the caller asks of
/// <see cref="WorkflowJobApiResponse.Conclusion"/> to decide what to build an excerpt from, and a
/// type that restated the answer could only ever repeat its caller's filter.</summary>
internal sealed record CiJob(long Id, string Name);
