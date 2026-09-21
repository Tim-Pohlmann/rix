namespace Rix.Submit;

/// <summary>Everything <c>rix submit</c> needs, already strongly typed — the CLI layer turns raw
/// flags into these values (see <see cref="Cli.SubmitCommand"/>).</summary>
internal record SubmitConfig
(
    RepoIdentifier Repo,
    GitToken WriteToken,
    // Reordering these two relative to each other compiles at every call site and silently swaps
    // where the bundles are read from with where the clone goes - they share a type, so nothing
    // catches it. That is why call sites pass them by name while the rest of the list stays
    // positional.
    DirectoryPath InputDir,
    DirectoryPath WorkDir
);
