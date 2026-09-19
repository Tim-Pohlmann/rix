namespace Rix.Submit;

/// <summary>Everything <c>rix submit</c> needs, already strongly typed — the CLI layer turns raw
/// flags into these values (see <see cref="Cli.SubmitCommand"/>).</summary>
internal record SubmitConfig(RepoIdentifier Repo, GitToken WriteToken, DirectoryPath InputDir, DirectoryPath WorkDir);
