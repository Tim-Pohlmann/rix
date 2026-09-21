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

    private const string ResourcePrefix = "Rix.Cli.Templates.";

    /// <summary>Each caller-workflow template as embedded, still carrying
    /// <see cref="RefPlaceholder"/>, paired with the repo-relative path it is written to, in the
    /// order <see cref="InitializeRunner"/> writes (and reports) them. Discovered from the embedded
    /// resources rather than a hand-kept list, so the <c>Cli/Templates/*.yml</c> glob in
    /// <c>Rix.csproj</c> is the only place the set is declared and a template added there cannot
    /// end up embedded but never written.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> All { get; } = Load();

    /// <summary>The caller-workflow templates pinned to <paramref name="reference"/>, each paired
    /// with the repo-relative path it is written to, in the order <see cref="InitializeRunner"/>
    /// writes (and reports) them.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> For(WorkflowRef reference)
    => [.. All.Select(template => (template.RelativePath, template.Content.Replace(RefPlaceholder, reference.Value, StringComparison.Ordinal)))];

    /// <summary>MSBuild names a resource <c>{RootNamespace}.{path with '/' replaced by '.'}</c> and
    /// leaves the file name itself alone, so stripping <see cref="ResourcePrefix"/> recovers the
    /// name verbatim - hyphens and all. Ordered by resource name because
    /// <see cref="Assembly.GetManifestResourceNames"/> promises no order of its own, and a stable
    /// one keeps the <c>wrote ...</c> lines identical from run to run.</summary>
    private static IReadOnlyList<(string RelativePath, string Content)> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
            throw new InvalidOperationException($"no embedded templates found under {ResourcePrefix}");
        return
        [
            .. resources.Select
            (
                name => ($".github/workflows/{name[ResourcePrefix.Length..]}", Read(assembly, name))
            )
        ];
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"embedded template not found: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
