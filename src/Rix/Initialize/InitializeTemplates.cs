using System.Reflection;

namespace Rix.Initialize;

/// <summary>The files <c>rix initialize</c> drops into a target repo, loaded from resources
/// embedded in this assembly (see <c>Rix.csproj</c>) so a released single-file binary carries them
/// with no network fetch at init time. Each <see cref="IInitializeSetup"/> has its own folder under
/// <c>Cli/Templates/</c>, laid out as the files land in the repo: the caller workflows under
/// <c>.github/workflows/</c>, and for a factory its agent home under <c>.rix/agent-home/</c>.</summary>
internal static class InitializeTemplates
{
    /// <summary>What the embedded templates carry after the <c>@</c> of their <c>uses:</c> lines in
    /// place of a real ref. A placeholder rather than a default ref the substitution overwrites, so
    /// a template that is never substituted fails visibly instead of quietly shipping whichever ref
    /// happened to be checked in.</summary>
    internal const string RefPlaceholder = "__RIX_REF__";

    /// <summary>What a template carries in place of the name of the workflow whose failures rix
    /// answers.</summary>
    internal const string CiWorkflowPlaceholder = "__RIX_CI_WORKFLOW__";

    /// <summary>What a <see cref="FactoryServedSetup"/>'s template carries in place of the factory
    /// repo it dispatches to.</summary>
    internal const string FactoryRepoPlaceholder = "__RIX_FACTORY_REPO__";

    internal const string ResourcePrefix = "Rix.Cli.Templates/";

    /// <summary>The templates of <paramref name="setup"/> with its placeholders filled in, each
    /// paired with the repo-relative path it is written to, in the order
    /// <see cref="InitializeRunner"/> writes (and reports) them.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> For(IInitializeSetup setup)
    {
        var (folder, values) = Layout(setup);
        return [.. Load(folder).Select(template => (template.RelativePath, Fill(template, values)))];
    }

    /// <summary>The values the templates' placeholders are filled in with. Every placeholder is
    /// filled in for every setup; one a setup has no value for is null, and a template of that
    /// setup must not carry it.</summary>
    /// <param name="Ref">What replaces <see cref="RefPlaceholder"/>.</param>
    /// <param name="CiWorkflow">What replaces <see cref="CiWorkflowPlaceholder"/>.</param>
    /// <param name="FactoryRepo">What replaces <see cref="FactoryRepoPlaceholder"/>.</param>
    private sealed record TemplateValues(RixRef? Ref = null, CiWorkflowName? CiWorkflow = null, RepoIdentifier? FactoryRepo = null);

    /// <summary>The folder under <c>Cli/Templates/</c> holding <paramref name="setup"/>'s templates,
    /// and the values their placeholders are filled in with.</summary>
    private static (string Folder, TemplateValues Values) Layout(IInitializeSetup setup) => setup switch
    {
        StandaloneSetup standalone => ("Standalone", new(Ref: standalone.Ref, CiWorkflow: standalone.CiWorkflow)),
        FactorySetup factory => ("Factory", new(Ref: factory.Ref)),
        FactoryServedSetup served => ("FactoryServed", new(CiWorkflow: served.CiWorkflow, FactoryRepo: served.FactoryRepo)),
        _ => throw new NotSupportedException($"Unexpected initialize setup type: {setup.GetType()}"),
    };

    private static string Fill((string RelativePath, string Content) template, TemplateValues values)
    {
        (string Placeholder, string? Value)[] fills =
        [
            (RefPlaceholder, values.Ref?.Value),
            (CiWorkflowPlaceholder, values.CiWorkflow?.Value),
            (FactoryRepoPlaceholder, values.FactoryRepo?.Value),
        ];
        return fills.Aggregate(template.Content, (content, fill) => FillIn(content, fill.Placeholder, fill.Value, template.RelativePath));
    }

    /// <summary>Replaces every <paramref name="placeholder"/> in <paramref name="content"/> with
    /// <paramref name="value"/>. A null value means the setup has none, so a template that still
    /// carries the placeholder is a bug in rix's templates, not in the user's input: it throws
    /// rather than writing a workflow with the placeholder left in.</summary>
    internal static string FillIn(string content, string placeholder, string? value, string relativePath)
    {
        if (value is not null)
            return content.Replace(placeholder, value, StringComparison.Ordinal);
        if (content.Contains(placeholder, StringComparison.Ordinal))
            throw new InvalidOperationException($"{relativePath} carries {placeholder}, but its setup has no value for it");
        return content;
    }

    /// <summary>Every template in <c>Cli/Templates/{folder}/</c>, read afresh on each call (an init
    /// writes once, so there is nothing to cache for), discovered from the embedded
    /// resources rather than a hand-kept list, so the glob in <c>Rix.csproj</c> is the only place a
    /// folder's file set is declared. <c>Rix.csproj</c> names each resource after its path below
    /// <c>Cli/Templates/</c>, so stripping the folder's prefix leaves the path the file is written
    /// to. MSBuild builds that name with the build machine's separator, so a Windows build's
    /// backslashes are turned back into slashes. Ordered by path because
    /// <see cref="Assembly.GetManifestResourceNames"/> promises no order of its own, and a stable one
    /// keeps the <c>wrote ...</c> lines identical from run to run.</summary>
    private static IReadOnlyList<(string RelativePath, string Content)> Load(string folder)
    {
        var prefix = $"{ResourcePrefix}{folder}/";
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Select(name => (Resource: name, Path: name.Replace('\\', '/')))
            .Where(resource => resource.Path.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(resource => resource.Path, StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
            throw new InvalidOperationException($"no embedded templates found under {prefix}");
        return [.. resources.Select(resource => (resource.Path[prefix.Length..], Read(assembly, resource.Resource)))];
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"embedded template not found: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
