using Rix.Agent;
using System.Reflection;

namespace Rix.Initialize;

/// <summary>The agent skills <c>rix initialize</c> writes into a factory repo's agent home
/// (<see cref="AgentHomePath"/>). The factory's workflows name the factory as their own
/// <c>factory-repo</c>, so rix copies that directory into the runner's home before the agent
/// starts, and the agent finds the skills where its harness looks. Only a factory gets them: the
/// one skill so far onboards a repo to the factory the run belongs to. Each skill is embedded once,
/// from <c>Cli/Skills/{name}.md</c> (see <c>Rix.csproj</c>), and written to every directory in
/// <see cref="SkillDirectories"/>.</summary>
internal static class AgentHomeSkills
{
    /// <summary>The agent home inside the factory repo: the one rix copies when the factory's
    /// workflows leave <c>agent-home-path</c> unset, as they do.</summary>
    internal const string AgentHomePath = AgentConfig.DefaultAgentHomePath;

    /// <summary>Where, relative to the home directory, the harnesses rix runs look for skills:
    /// Claude Code reads only <c>~/.claude/skills</c> and pi only <c>~/.agents/skills</c>. opencode
    /// reads both, so it finds each skill twice, under the same name.</summary>
    internal static IReadOnlyList<string> SkillDirectories { get; } = [".claude/skills", ".agents/skills"];

    internal const string ResourcePrefix = "Rix.Cli.Skills.";

    private const string ResourceSuffix = ".md";

    /// <summary>The skill files of <paramref name="setup"/>, each paired with the repo-relative path
    /// it is written to: every embedded skill in every one of <see cref="SkillDirectories"/> for a
    /// factory, and nothing for any other setup.</summary>
    internal static IReadOnlyList<(string RelativePath, string Content)> For(IInitializeSetup setup) => setup switch
    {
        FactorySetup => InEverySkillDirectory(),
        StandaloneSetup or FactoryServedSetup => [],
        _ => throw new NotSupportedException($"Unexpected initialize setup type: {setup.GetType()}"),
    };

    private static IReadOnlyList<(string RelativePath, string Content)> InEverySkillDirectory()
    => [
        .. from skill in Load()
           from directory in SkillDirectories
           select ($"{AgentHomePath}/{directory}/{skill.Name}/SKILL.md", skill.Content)
    ];

    /// <summary>Every embedded skill, named after its file. Discovered from the resources and
    /// ordered, as <see cref="WorkflowTemplates"/> does: MSBuild leaves a file name's hyphens alone,
    /// so the name between the prefix and the suffix is the skill's directory name verbatim.</summary>
    private static IReadOnlyList<(string Name, string Content)> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
            throw new InvalidOperationException($"no embedded skills found under {ResourcePrefix}");
        return [.. resources.Select(name => (name[ResourcePrefix.Length..^ResourceSuffix.Length], Read(assembly, name)))];
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"embedded skill not found: {resource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
