namespace Rix.Repository;

/// <summary>Thrown by every host here — <see cref="GitHubJobRepoHost"/>, <see cref="GitHubSubmitRepoHost"/>
/// and <see cref="GitHubCiFailureRepoHost"/> — when an operation fails for an operational reason the
/// caller cannot recover from: a git command that
/// exited non-zero, an unreachable or error-returning GitHub API, or a malformed API response.
/// The message names the operation that failed; callers never branch on <em>why</em>, they catch
/// this one type once at their entry point and turn it into their own failure result. Genuine
/// programming errors keep surfacing as other exception types and are deliberately not folded in
/// here, so a boundary <c>catch (RepoHostException)</c> can't swallow them. Keeps a generic
/// name where the repo hosts are named for their subcommand, because every one of them throws it
/// and every caller catches it the same way.</summary>
internal sealed class RepoHostException(string message, Exception? innerException = null)
    : Exception(message, innerException);
