namespace Rix.Submit;

/// <summary>Everything <c>rix submit</c> needs, already strongly typed — the CLI layer turns raw
/// flags into these values (see <see cref="Cli.SubmitCommand"/>).</summary>
/// <param name="AllowedPushBranches">The branches a pending push may deliver to. Empty — the
/// default — rejects every push, so an operator opts in by naming the branches this submit may
/// touch. Deliberately duplicates the list <c>rix job</c> gave its <c>/push</c> endpoint rather
/// than reading it back out of <c>result.json</c>: that file sits in the agent's own workspace and
/// the agent can rewrite it, so a list taken from it would be the attacker's list. Supplied by the
/// caller that holds the write credential instead.</param>
internal record SubmitConfig
(
    RepoIdentifier Repo,
    GitToken WriteToken,
    // Reordering these two relative to each other compiles at every call site and silently swaps
    // where the bundles are read from with where the clone goes - they share a type, so nothing
    // catches it. That is why call sites pass them by name while the rest of the list stays
    // positional.
    DirectoryPath InputDir,
    DirectoryPath WorkDir,
    IReadOnlyList<BranchName> AllowedPushBranches
);
