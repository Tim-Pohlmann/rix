using Rix.Job;
using Rix.Process;

namespace Rix.Agents;

/// <summary>
/// A pluggable coding agent (e.g. Claude Code). Captures everything agent-specific behind a
/// single boundary: how to install the CLI, how to launch it for a job, and how to read the
/// run's cost from its output. The job core (<see cref="JobRunner.RunAsync"/>) depends only on
/// this interface, so swapping agents requires no core changes.
/// </summary>
internal interface ICodingAgent
{
    /// <summary>
    /// Ensures the agent CLI is available, installing it if necessary. All subprocesses run
    /// through the injected <paramref name="runProcess"/> so installation stays on the same
    /// side-effect seam as the rest of the job.
    /// </summary>
    Task<InstallResult> EnsureInstalledAsync(RunProcessAsync runProcess, CancellationToken cancellationToken);

    /// <summary>Builds the process invocation that launches the agent for this job (pure).</summary>
    AgentInvocation BuildInvocation(JobConfig config, string systemPrompt);

    /// <summary>
    /// Extracts the run's cumulative USD cost from a single agent output line, or <c>null</c>
    /// when the line carries no cost (pure).
    /// </summary>
    decimal? ParseCost(string outputLine);
}

/// <summary>A pure description of how to launch a coding agent as a subprocess.</summary>
internal sealed record AgentInvocation(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentOverrides);

/// <summary>Outcome of <see cref="ICodingAgent.EnsureInstalledAsync"/>.</summary>
internal abstract record InstallResult
{
    private protected InstallResult() { }
}
internal sealed record Installed : InstallResult;
internal sealed record InstallFailed(string Reason) : InstallResult;
