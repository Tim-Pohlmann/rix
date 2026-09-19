namespace Rix.Repository;

/// <summary>Thrown by <see cref="ICiHost"/> implementations when reading a run fails for an
/// operational reason the caller cannot recover from: an unreachable or error-returning CI API, or
/// a malformed response. The repo-side twin of <see cref="RepoHostException"/>, and separate from it
/// so a failure names the system that actually failed — once the CI provider and the repo host are
/// different vendors, "the repo host errored" would be the wrong thing to go looking at.
///
/// Like its twin, the message names the operation that failed and callers never branch on
/// <em>why</em>; they catch it once at their entry point and turn it into their own failure result.
/// Genuine programming errors keep surfacing as other exception types and are deliberately not
/// folded in here.</summary>
internal sealed class CiHostException(string message, Exception? innerException = null)
    : Exception(message, innerException);
