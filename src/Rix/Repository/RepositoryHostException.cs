namespace Rix.Repository;

/// <summary>Thrown by <see cref="GitHubReadHost"/> / <see cref="GitHubHost"/> when a repository-host
/// operation fails for an operational reason the caller cannot recover from: a git command that
/// exited non-zero, an unreachable or error-returning GitHub API, or a malformed API response.
/// The message names the operation that failed; callers never branch on <em>why</em>, they catch
/// this one type once at their entry point and turn it into their own failure result. Genuine
/// programming errors keep surfacing as other exception types and are deliberately not folded in
/// here, so a boundary <c>catch (RepositoryHostException)</c> can't swallow them.</summary>
internal sealed class RepositoryHostException(string message, Exception? innerException = null)
    : Exception(message, innerException);
