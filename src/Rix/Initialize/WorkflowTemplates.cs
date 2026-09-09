using System.Reflection;

namespace Rix.Initialize;

/// <summary>The caller-workflow files <c>rix initialize</c> drops into a target repo, loaded from
/// resources embedded in this assembly (see <c>Rix.csproj</c>) so a released single-file binary
/// carries them with no network fetch at init time.</summary>
internal static class WorkflowTemplates
{
    /// <summary>Each caller-workflow template paired with the repo-relative path it is written to,
    /// in the order <see cref="InitializeRunner"/> writes (and reports) them.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> All { get; } =
    [
        (".github/workflows/rix.yml", Read("rix.yml")),
        (".github/workflows/rix-on-ci-failure.yml", Read("rix-on-ci-failure.yml")),
    ];

    private static string Read(string fileName)
    {
        var resource = $"Rix.Cli.Templates.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"embedded template not found: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
