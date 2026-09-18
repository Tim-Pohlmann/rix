using Rix.Job;

namespace Rix.CiFailure;

/// <summary>Combines a <see cref="CiFailureConfig"/> (how to check a run) with a
/// <see cref="JobConfig"/> (how to run the agent) for <c>rix ci-failure-job</c>, which checks a run
/// and, only if it failed, runs the agent against the prompt built from that failure. The job
/// config's prompt is <see cref="PlaceholderPrompt"/> until <see cref="JobConfig.WithPrompt"/>
/// substitutes the real one once a failure is actually detected, and its allow-list is likewise
/// derived from the failing run's branch by <see cref="CiFailureJobRunner"/> rather than taken
/// from the caller (see <see cref="JobConfig.WithAllowedPushBranches"/>).</summary>
internal sealed record CiFailureJobConfig(CiFailureConfig CiFailure, JobConfig Job)
{
    /// <summary>Never seen by anything real: <see cref="Job"/> is only used via
    /// <see cref="JobConfig.WithPrompt"/> once a failure is detected, which always replaces it
    /// first.</summary>
    internal const string PlaceholderPrompt = "(pending ci-failure detection)";
}
