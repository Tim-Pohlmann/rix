using Rix.Agent;
using Rix.Process;

namespace Rix.CodingAgents;

/// <summary>
/// A pluggable coding agent (e.g. Claude Code). Captures everything agent-specific behind a
/// single boundary: how to install the CLI, how to launch it for a run, and how to read the
/// run's cost from its output. The core (<see cref="AgentRunner.RunAsync"/>) depends only on
/// this interface, so swapping agents requires no core changes.
/// </summary>
internal interface ICodingAgent
{
    /// <summary>
    /// Ensures the agent CLI is available, installing it if necessary. All subprocesses run
    /// through the injected <paramref name="processRunner"/> so installation stays on the same
    /// side-effect seam as the rest of the run, from <paramref name="workingDirectory"/>, a scratch
    /// directory outside any repo.
    /// </summary>
    /// <param name="reportDiagnostic">Where what the installer writes to stderr goes - npm's
    /// warnings, and whatever it says about a failure the reason only summarizes. An
    /// <see cref="InstallFailed"/> names the command that failed; this is how the detail behind it
    /// reaches whoever is watching the run.</param>
    Task<InstallResult> EnsureInstalledAsync
    (
        IProcessRunner processRunner,
        DirectoryPath workingDirectory,
        Action<string> reportDiagnostic,
        CancellationToken cancellationToken
    );

    /// <summary>Builds the process invocation that launches the agent for this run (pure).</summary>
    AgentInvocation BuildInvocation(AgentConfig config, string systemPrompt);

    /// <summary>
    /// Extracts the USD cost a single agent output line adds to the run, or <c>null</c> when the
    /// line carries no cost (pure). Every stdout line is offered, and the run's cost is the sum,
    /// so an agent that reports a cumulative total must report it on one line only.
    /// </summary>
    decimal? ParseCost(string outputLine);

    /// <summary>
    /// Extracts the human-readable content of a single agent output line — assistant text and
    /// tool-call markers — or <c>null</c> when the line carries nothing worth transcribing
    /// (e.g. system noise, user/tool-result feedback, or the cost-bearing result line, which
    /// <see cref="ParseCost"/> already consumes). Pure.
    /// </summary>
    string? ParseTranscriptLine(string outputLine);
}

/// <summary>A pure description of how to launch a coding agent as a subprocess.</summary>
internal sealed record AgentInvocation
(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentOverrides
);

/// <summary>Outcome of <see cref="ICodingAgent.EnsureInstalledAsync"/>.</summary>
internal abstract record InstallResult
{
    private protected InstallResult() { }
}
internal sealed record Installed : InstallResult;
internal sealed record InstallFailed(string Reason) : InstallResult;

/// <summary>Shared helper for <see cref="ICodingAgent"/> implementations.</summary>
internal static class CodingAgentHelper
{
    /// <summary>
    /// Runs a command in <paramref name="workingDirectory"/> and returns <c>null</c> on success, or
    /// the failure reason on error. The one place the directory is unwrapped: a bare path is all a
    /// process can be started in.
    /// </summary>
    private static async Task<string?> RunCommandAsync
    (
        IProcessRunner processRunner,
        DirectoryPath workingDirectory,
        string fileName,
        IEnumerable<string> args,
        Action<string> reportDiagnostic,
        CancellationToken cancellationToken
    )
    {
        // No stdout sink: what these commands print is a version string or npm's progress, and all
        // this needs from them is whether they worked.
        var result = await processRunner.RunAsync
        (
            fileName, args, workingDirectory.Value, null, null, reportDiagnostic, cancellationToken
        );
        if (result is ProcessFailure failure)
            return failure.Reason;
        return null;
    }

    /// <summary>
    /// Ensures a CLI tool is available by checking if it is already installed, and if not,
    /// installing it via <c>npm install -g</c>. Returns <see cref="Installed"/> on success,
    /// <see cref="InstallFailed"/> on any error.
    /// </summary>
    internal static async Task<InstallResult> EnsureInstalledViaNpmAsync
    (
        IProcessRunner processRunner,
        DirectoryPath workingDirectory,
        string cliName,
        string npmPackage,
        Action<string> reportDiagnostic,
        CancellationToken cancellationToken
    )
    {
        Task<string?> Run(string fileName, IEnumerable<string> args)
        => RunCommandAsync(processRunner, workingDirectory, fileName, args, reportDiagnostic, cancellationToken);

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
