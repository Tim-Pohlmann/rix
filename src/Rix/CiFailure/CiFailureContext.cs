using Rix.Job;
using Rix.Repository;

namespace Rix.CiFailure;

/// <summary>The side-effecting collaborators <c>rix ci-failure</c> needs: the host it checks the
/// run with, and the <see cref="JobContext"/> the agent run gets once a failure has been detected.
/// Stands to <see cref="JobContext"/> exactly as <see cref="CiFailureConfig"/> stands to
/// <see cref="JobConfig"/> — this command's are the job's plus the one role only it has — so
/// <see cref="CiFailureRunner.RunAsync"/> takes a config and a context just as
/// <see cref="JobRunner.RunAsync"/> does, rather than a loose host alongside a bundle.
///
/// Composed rather than flattened into the job's five fields plus a <c>ToJobContext()</c>: the
/// runner reads each half exactly once, so nothing pays for the extra hop, while a positional copy
/// between two same-shaped records is a swap waiting to happen.
///
/// In production both halves ride one shared <see cref="Repository.GitHubApi"/> transport — the
/// ci-failure check and the job's clone are two roles against the same repo under the same
/// credential — while staying separate fields so a test can stub either role on its own.</summary>
/// <param name="JobFor">A factory rather than a ready-made <see cref="JobContext"/> because a
/// context needs the agent the job will run with, which the <see cref="JobConfig"/> names — and
/// that config only exists once a detected failure has supplied the prompt, since
/// <see cref="CiFailureConfig.ToJobConfig"/> cannot build one before then. Never invoked when the
/// run hadn't failed.</param>
internal sealed record CiFailureContext(IGitHubCiFailureHost CiFailureHost, Func<JobConfig, JobContext> JobFor);
