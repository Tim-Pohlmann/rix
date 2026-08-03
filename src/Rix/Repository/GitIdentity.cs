namespace Rix.Repository;

/// <summary>The single source of truth for the git commit identity <c>rix job</c> configures in the
/// cloned repo before handing it to the coding agent, so the agent never has to guess author
/// metadata. Used by <see cref="GitHubReadHost.ConfigureGitAsync"/>.</summary>
internal static class GitIdentity
{
    internal const string Name = "rix";
    internal const string Email = "rix@noreply.invalid";
}
