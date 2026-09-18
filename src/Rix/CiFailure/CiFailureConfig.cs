namespace Rix.CiFailure;

/// <summary>Everything <c>rix ci-failure</c> needs to look up one workflow run, already strongly
/// typed — the CLI layer turns raw flags into these values (see <see cref="Cli.CiFailureOptions"/>).</summary>
internal sealed record CiFailureConfig(RepoIdentifier Repo, GitReadToken ReadToken, long RunId);
