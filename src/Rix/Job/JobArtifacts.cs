using System.Text.Json;

namespace Rix.Job;

/// <summary>What a job leaves in its output directory: <c>result.json</c> always, and
/// <c>transcript.md</c> when the agent said anything worth keeping. These are the job's product
/// rather than the CLI's presentation of it - <c>rix submit</c> reads <c>result.json</c> from the
/// directory a job wrote it to and refuses an input directory without one - so
/// <see cref="JobRunner.RunAsync"/> produces them as part of running, and a job that returns is a
/// job whose output is on disk.
///
/// <c>result.json</c> is written whatever the outcome, including a <see cref="SetupFailure"/>
/// before the agent ran, so there is one place to read the outcome from instead of scraping stdout.
///
/// Named for the files rather than for the outcome: <see cref="IJobResult"/> is what happened, this
/// is what the job leaves behind about it.</summary>
internal static class JobArtifacts
{
    /// <summary>The result as JSON - the one spelling of it, so what lands in
    /// <c>result.json</c> and what the CLI prints to stdout cannot drift apart.</summary>
    internal static string Serialize(IJobResult result)
    => JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);

    internal static async Task WriteAsync
    (
        JobConfig config, JobContext context, IJobResult result, IReadOnlyList<string> transcript
    )
    {
        await WriteArtifactAsync(config, context, "result.json", Serialize(result));
        // Skipped entirely when nothing was extracted, so the artifact only exists when there is
        // content: joined with a blank line, it is a human-readable log of what the agent said.
        if (transcript.Count > 0)
            await WriteArtifactAsync(config, context, "transcript.md", string.Join("\n\n", transcript));
    }

    /// <summary>Writes one artifact, reporting a failure instead of raising it and taking no
    /// <see cref="CancellationToken"/>. By the time these run the outcome is already decided - the
    /// agent has run, the money is spent, the bundles exist - so a full disk, an unwritable output
    /// directory or a cancellation arriving in that window must not change what the job reports.
    /// Only the file is lost.</summary>
    private static async Task WriteArtifactAsync
    (
        JobConfig config, JobContext context, string name, string content
    )
    {
        try
        {
            await context.FileSystem.WriteAllTextAsync
            (
                Path.Combine(config.OutputDir.Value, name), content, CancellationToken.None
            );
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn(context.LogLine, $"warning: failed to write {name}: {ex.Message}");
        }
    }

    /// <summary>Reports <paramref name="line"/> through <paramref name="log"/>, swallowing the ways
    /// a closed or broken sink can fail - the warning about a lost artifact must not cost the
    /// correct result any more than the lost artifact itself did.</summary>
    private static void Warn(LogLine log, string line)
    {
        try { log(line); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* nothing left to report to */ }
    }
}
