using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Cli;

/// <summary>The two options every command takes, whether or not it runs an agent: which repo to
/// act on and where to put the temp clone. Declared once here rather than per command, so a change
/// to a flag's description, its environment fallback, or how its text becomes a value reaches all
/// of them — <c>job</c> and <c>ci-failure</c> register these through <see cref="JobOptions.AddTo"/>
/// alongside the agent-running options, and <c>submit</c> registers them directly, being the one
/// command that needs these two and nothing else from that set.</summary>
internal static class CommonOptions
{
    internal static readonly Option<string> RepoOption = new
    (
        name: "--repo",
        description: "Full GitHub repo identifier (owner/repo)"
    )
    { IsRequired = false };

    internal static readonly Option<string> WorkDirOption = new
    (
        name: "--work-dir",
        description: "Base directory for the temp clone (default: system temp)"
    )
    { IsRequired = false };

    internal static RepoIdentifier ReadRepo(ParseResult parsed)
    => parsed.Required(RepoOption, "RIX_REPO", value => new RepoIdentifier(value));

    /// <summary>The system temp directory is the fallback, built lazily so an explicitly supplied
    /// <c>--work-dir</c> never pays for a <see cref="DirectoryPath"/> it won't use.</summary>
    internal static DirectoryPath ReadWorkDir(ParseResult parsed, IFileSystem fileSystem)
    => parsed.Optional
    (
        WorkDirOption,
        "RIX_WORK_DIR",
        path => new DirectoryPath(path, fileSystem),
        () => new DirectoryPath(Path.GetTempPath(), fileSystem)
    );
}
