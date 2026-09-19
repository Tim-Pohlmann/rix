using System.Reflection;

namespace Rix.Initialize;

/// <summary>The caller-workflow files <c>rix initialize</c> drops into a target repo, loaded from
/// resources embedded in this assembly (see <c>Rix.csproj</c>) so a released single-file binary
/// carries them with no network fetch at init time.</summary>
internal static class WorkflowTemplates
{
    /// <summary>What the embedded templates carry after the <c>@</c> of their <c>uses:</c> lines in
    /// place of a real ref. A placeholder rather than a default ref the substitution overwrites, so
    /// a template that is never substituted fails visibly instead of quietly shipping whichever ref
    /// happened to be checked in.</summary>
    internal const string RefPlaceholder = "__RIX_REF__";

    private static readonly string[] FileNames = ["rix.yml", "rix-on-ci-failure.yml"];

    private static readonly (string RelativePath, string Content)[] Embedded =
        [.. FileNames.Select(name => ($".github/workflows/{name}", Read(name)))];

    /// <summary>The caller-workflow templates pinned to <paramref name="reference"/>, each paired
    /// with the repo-relative path it is written to, in the order <see cref="InitializeRunner"/>
    /// writes (and reports) them.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> For(WorkflowRef reference)
    => [.. Embedded.Select(template => (template.RelativePath, template.Content.Replace(RefPlaceholder, reference.Value, StringComparison.Ordinal)))];

    private static string Read(string fileName)
    {
        var resource = $"Rix.Cli.Templates.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"embedded template not found: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
