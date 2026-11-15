namespace Rix.Initialize;

/// <summary>
/// Writes the files of <see cref="InitializeConfig.Setup"/> (<see cref="FilesFor"/>) into the
/// target repo, overwriting any file that is already there. This is the whole
/// job of <c>rix initialize</c>: the reusable workflows the callers invoke live in the rix repo
/// and are referenced by <c>uses:</c> at the setup's ref, not copied here. What is left to the
/// user (<see cref="NextSteps"/>) is logged once every file is written.
/// </summary>
internal static class InitializeRunner
{
    internal static async Task<IInitializeResult> RunAsync
    (
        InitializeConfig config,
        InitializeContext context,
        CancellationToken cancellationToken
    )
    {
        foreach (var (relativePath, content) in FilesFor(config.Setup))
        {
            var fullPath = Path.Combine(config.TargetDir.Value, relativePath);
            try
            {
                await context.FileSystem.WriteAllTextAsync(fullPath, content, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new InitializeFailure($"could not write {relativePath}: {ex.Message}");
            }
            context.Log.WriteLine($"wrote {relativePath}");
        }
        context.Log.WriteLine(NextSteps.For(config.Setup));
        return new InitializeSuccess();
    }

    /// <summary>What <paramref name="setup"/> writes, in the order it is written and reported: the
    /// caller workflows into <c>.github/workflows/</c>, then the skills of a factory's agent
    /// home.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> FilesFor(IInitializeSetup setup)
    => [.. WorkflowTemplates.For(setup), .. AgentHomeSkills.For(setup)];
}
