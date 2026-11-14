using Rix.Agent;
using System.Text.Json;

namespace Rix.Artifacts;

/// <summary>What a run leaves in its output directory: <c>result.json</c> always, and
/// <c>transcript.md</c> when the agent said anything worth keeping. These are the run's product
/// rather than the CLI's presentation of it - <c>rix submit</c> reads <c>result.json</c> from the
/// directory a run wrote it to and refuses an input directory without one - so
/// <see cref="AgentRunner.RunAsync"/> produces them as part of running, and a run that returns is a
/// run whose output is on disk.
///
/// <c>result.json</c> is written whatever the outcome, including a <see cref="SetupFailure"/>
/// before the agent ran, so there is one place to read the outcome from instead of scraping stdout.
/// It is also the one artifact whose loss is reported rather than warned about - see
/// <see cref="WriteAsync"/>.
///
/// Named for the files rather than for the outcome: <see cref="IAgentResult"/> is what happened, this
/// is what the run leaves behind about it.</summary>
internal static class AgentArtifacts
{
    /// <summary>The result as JSON - the one spelling of it, so what lands in
    /// <c>result.json</c> and what the CLI prints to stdout cannot drift apart.</summary>
    internal static string Serialize(IAgentResult result)
    => JsonSerializer.Serialize(result, AgentJsonContext.Default.IAgentResult);

    /// <summary>Writes both and returns what became of <c>result.json</c>, the one of the two a
    /// caller has to know about: <c>rix submit</c> reads it back out of this directory and refuses
    /// a directory without one, so losing it leaves the run unfinished however well the agent itself
    /// went. <c>transcript.md</c> is an echo of what is already in the log, so the warning
    /// <see cref="ArtifactFile"/> emits is the whole of what its loss costs and there is nothing
    /// here to return about it.</summary>
    internal static async Task<IArtifactWrite> WriteAsync
    (
        AgentConfig config, AgentContext context, IAgentResult result, IReadOnlyList<string> transcript
    )
    {
        var resultFile = await Write(config, context, "result.json", Serialize(result));
        // Skipped entirely when nothing was extracted, so the artifact only exists when there is
        // content: joined with a blank line, it is a human-readable log of what the agent said.
        if (transcript.Count > 0)
            await Write(config, context, "transcript.md", string.Join("\n\n", transcript));

        return resultFile;
    }

    /// <summary>One artifact into the run's output directory, under
    /// <see cref="ArtifactFile"/>'s rule: a write that cannot throw and cannot be cancelled. Here
    /// only to spare both call sites the same four fields out of <paramref name="config"/> and
    /// <paramref name="context"/>.</summary>
    private static Task<IArtifactWrite> Write(AgentConfig config, AgentContext context, string name, string content)
    => ArtifactFile.WriteAsync(context.FileSystem, context.Log, config.OutputDir, name, content);
}
