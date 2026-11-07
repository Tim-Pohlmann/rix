using Rix.CiFailure;
using System.Text.Json;

namespace Rix.Artifacts;

/// <summary>What a check leaves in its output directory: <c>result.json</c> always, and
/// <c>prompt.md</c> when a failure was detected. The counterpart of <see cref="Job.JobArtifacts"/>
/// for <c>rix ci-failure</c> - the files are the check's product rather than the CLI's
/// presentation of them, so <see cref="CiFailureRunner.RunAsync"/> produces them as part of
/// checking, and a check that returns is a check whose output is on disk.
///
/// The two files follow different rules, and which rule applies is the whole content of this
/// class. <c>prompt.md</c> is written before the verdict is final and a failed write changes it:
/// the verdict names the file rather than carrying its text, so a <c>detected</c> whose prompt
/// never reached disk would send a caller to a file that isn't there, and reporting a
/// <see cref="CiFailureError"/> instead leaves it with no branch and no agent started.
/// <c>result.json</c> is written after, records a verdict already decided, and its loss costs
/// nothing but a warning - unlike a job's <c>result.json</c>, which is the file something
/// downstream reads back. Nothing reads this one: <c>rix ci-failure</c> prints the same JSON to
/// stdout, which is where the action gating a run on it reads the verdict, and the file is there
/// for a caller who would rather read a file.</summary>
internal static class CiFailureArtifacts
{
    /// <summary>The verdict as JSON - the one spelling of it, so what lands in <c>result.json</c>
    /// and what the CLI prints to stdout cannot drift apart.</summary>
    internal static string Serialize(ICiFailureResult result)
    => JsonSerializer.Serialize(result, CiFailureJsonContext.Default.ICiFailureResult);

    /// <summary>Where the prompt goes, so that the name of the file and the path the verdict
    /// reports are the same decision made once. Known before anything is written, which is what
    /// lets <see cref="CiFailureDetected.PromptFile"/> be set when the verdict is built instead of
    /// being patched into it afterwards.</summary>
    internal static string PromptFile(DirectoryPath outputDir)
    => Path.Combine(outputDir.Value, "prompt.md");

    /// <summary>Writes what <paramref name="result"/> calls for and returns the verdict as
    /// recorded, which is the one a caller should act on: the same verdict, unless writing
    /// <c>prompt.md</c> failed and it became a <see cref="CiFailureError"/>.</summary>
    internal static async Task<ICiFailureResult> WriteAsync
    (
        CiFailureConfig config, CiFailureContext context, ICiFailureResult result
    )
    {
        var recorded = result;
        if (result is CiFailureDetected detected)
            recorded = await WritePromptAsync(detected, context.FileSystem);

        await ArtifactFile.WriteAsync
        (
            context.FileSystem, context.Log, config.OutputDir, "result.json", Serialize(recorded)
        );
        return recorded;
    }

    private static async Task<ICiFailureResult> WritePromptAsync(CiFailureDetected detected, IFileSystem fileSystem)
    {
        try
        {
            await fileSystem.WriteAllTextAsync(detected.PromptFile, detected.Prompt, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CiFailureError($"failed to write the prompt to {detected.PromptFile}: {ex.Message}");
        }

        return detected;
    }
}
