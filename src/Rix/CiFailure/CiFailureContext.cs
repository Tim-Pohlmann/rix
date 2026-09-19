using Rix.Job;
using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>The side-effecting collaborators <c>rix ci-failure</c> needs: the CI host it reads the
/// run from, the repo host it judges that run against, and the <see cref="JobContext"/> the agent
/// run gets once a failure has been detected.
/// Stands to <see cref="JobContext"/> exactly as <see cref="CiFailureConfig"/> stands to
/// <see cref="JobConfig"/> — this command's are the job's plus the one role only it has — so
/// <see cref="CiFailureRunner.RunAsync"/> takes a config and a context just as
/// <see cref="JobRunner.RunAsync"/> does, rather than a loose repo host alongside a bundle.
///
/// Composed rather than flattened into the job's five fields plus a <c>ToJobContext()</c>: the
/// runner reads each half exactly once, so nothing pays for the extra hop, while a positional copy
/// between two same-shaped records is a swap waiting to happen.
///
/// In production all three ride one shared <see cref="Repository.GitHubApi"/> transport — on GitHub
/// the CI provider and the repo host are the same service, and the ci-failure check and the job's
/// clone are roles against the same repo under the same credential — while staying separate fields
/// so a test can stub any role on its own, and so a setup whose CI isn't GitHub Actions can supply
/// a <see cref="ICiHost"/> that talks to something else entirely.</summary>
/// <param name="Job">Ready-made rather than built per <see cref="JobConfig"/>: the only thing a
/// <see cref="JobContext"/> takes from its config is which agent to run, and
/// <see cref="CiFailureConfig.Agent"/> names that up front — unlike the prompt, which only a
/// detected failure can supply. A run that hadn't failed leaves it unused, which costs nothing:
/// building one opens no connection and starts no process.</param>
internal sealed record CiFailureContext(ICiHost Ci, ICiFailureRepoHost RepoHost, JobContext Job);
