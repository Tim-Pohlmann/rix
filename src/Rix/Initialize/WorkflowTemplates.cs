using System.Reflection;

namespace Rix.Initialize;

/// <summary>The caller-workflow files <c>rix initialize</c> drops into a target repo, loaded from
/// resources embedded in this assembly (see <c>Rix.csproj</c>) so a released single-file binary
/// carries them with no network fetch at init time.</summary>
internal static class WorkflowTemplates
{
    private static readonly string[] FileNames = ["rix.yml", "rix-on-ci-failure.yml"];

    /// <summary>Each caller-workflow template paired with the repo-relative path it is written to,
    /// in the order <see cref="InitializeRunner"/> writes (and reports) them.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> All { get; } =
        [.. FileNames.Select(name => ($".github/workflows/{name}", Read(name)))];

    private static string Read(string fileName)
    {
        var resource = $"Rix.Cli.Templates.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"embedded template not found: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
