using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>The side-effecting collaborators <c>rix ci-failure</c> needs: the CI host it reads the
/// run from, and the repo host it judges that run against. No <see cref="Job.JobContext"/> among
/// them — detection stops at a verdict, so nothing here can clone, run a process, or write to a
/// remote.
///
/// In production both ride one shared <see cref="Repository.GitHubApi"/> transport — on GitHub the
/// CI provider and the repo host are the same service under the same credential — while staying
/// separate fields so a test can stub either role on its own, and so a setup whose CI isn't GitHub
/// Actions can supply an <see cref="ICiHost"/> that talks to something else entirely.</summary>
internal sealed record CiFailureContext(ICiHost Ci, ICiFailureRepoHost RepoHost);
