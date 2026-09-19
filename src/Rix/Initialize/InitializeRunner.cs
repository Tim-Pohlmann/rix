namespace Rix.Initialize;

/// <summary>
/// Writes the caller-workflow templates (<see cref="WorkflowTemplates.All"/>) into the target
/// repo's <c>.github/workflows/</c>, overwriting any file that is already there. This is the whole
/// job of <c>rix initialize</c>: the reusable workflows the callers invoke live in the rix repo
/// and are referenced by <c>uses:</c>, not copied here.
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
        foreach (var (relativePath, content) in WorkflowTemplates.All)
        {
            var fullPath = Path.Combine(config.TargetDir.Value, relativePath);
            try
            {
                await context.WriteFile(fullPath, content, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new InitializeFailure($"could not write {relativePath}: {ex.Message}");
            }
            context.LogLine($"wrote {relativePath}");
        }
        return new InitializeSuccess();
    }
}
