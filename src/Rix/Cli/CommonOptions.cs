using System.CommandLine;

namespace Rix.Cli;

/// <summary>The two options every command takes, whether or not it runs an agent: which repo to
/// act on and where to put the temp clone. Declared once here rather than per command, so a change
/// to a flag's description, its environment fallback, or how its text becomes a value reaches all
/// of them — <c>job</c> registers these through <see cref="JobOptions.AddTo"/> alongside the
/// agent-running options, <c>submit</c> registers both directly, and <c>ci-failure</c>, which
/// clones nothing, registers only the repo.</summary>
internal static class CommonOptions
{
    internal static readonly Option<string> RepoOption = new("--repo")
    {
        Description = "Full GitHub repo identifier (owner/repo)"
    };

    internal static readonly Option<string> WorkDirOption = new("--work-dir")
    {
        Description = "Base directory for the temp clone (default: a private temp directory of rix's own)"
    };

    internal static RepoIdentifier ReadRepo(ParseResult parsed)
    => parsed.Required(RepoOption, "RIX_REPO", value => new RepoIdentifier(value));

    /// <summary>The directory the caller named, or <c>null</c> when neither the flag nor the
    /// environment named one. Supplying the default is <see cref="Startup"/>'s job rather than this
    /// one's, because producing it means creating a directory and the commands never touch the file
    /// system - the same split that keeps <c>--prompt-file</c>'s contents out of here.</summary>
    internal static DirectoryPath? ReadWorkDir(ParseResult parsed)
    => parsed.Optional<DirectoryPath?>
    (
        WorkDirOption,
        "RIX_WORK_DIR",
        path => new DirectoryPath(path),
        () => null
    );
}
