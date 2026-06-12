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

/// <summary>Shared helpers for <see cref="ICodingAgent"/> implementations.</summary>
internal static class CodingAgentHelper
{
    /// <summary>
    /// Runs a command and returns <c>null</c> on success, or the failure reason on error.
    /// </summary>
    internal static async Task<string?> RunCommandAsync(
        RunProcessAsync runProcess,
        string fileName,
        IEnumerable<string> args,
        CancellationToken cancellationToken)
    {
        var result = await runProcess(fileName, args, Path.GetTempPath(), null, null, cancellationToken);
        return result is ProcessFailure failure ? failure.Reason : null;
    }

    /// <summary>
    /// Ensures a CLI tool is available by checking if it is already installed, and if not,
    /// installing it via <c>npm install -g</c>. Returns <see cref="Installed"/> on success,
    /// <see cref="InstallFailed"/> on any error.
    /// </summary>
    internal static async Task<InstallResult> EnsureInstalledViaNpmAsync(
        RunProcessAsync runProcess,
        string cliName,
        string npmPackage,
        CancellationToken cancellationToken)
    {
        Task<string?> Run(string fileName, IEnumerable<string> args) =>
            RunCommandAsync(runProcess, fileName, args, cancellationToken);

        if (await Run(cliName, ["--version"]) is null) return new Installed();

        if (await Run("npm", ["--version"]) is { } npmReason)
            return new InstallFailed($"{cliName} is not installed and npm could not be run ({npmReason}). Install Node.js to continue.");

        if (await Run("npm", ["install", "-g", npmPackage]) is { } installReason)
            return new InstallFailed($"npm install -g {npmPackage} failed ({installReason}).");

        // Re-verify: npm install can succeed but the CLI may still not be on PATH.
        if (await Run(cliName, ["--version"]) is { } verifyReason)
            return new InstallFailed($"{cliName} was installed but could not be verified ({verifyReason}).");

        return new Installed();
    }
}
