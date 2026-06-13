using System.Diagnostics;
using System.Text.Json.Serialization;
using Rix.Agents;
using Rix.Api;
using Rix.Process;

namespace Rix.Job;

[JsonSerializable(typeof(IJobResult))]
[JsonSerializable(typeof(JobSuccess))]
[JsonSerializable(typeof(JobFailure))]
[JsonSerializable(typeof(SetupFailure))]
[JsonSerializable(typeof(PendingPr))]
internal partial class JobJsonContext : JsonSerializerContext { }

internal static class JobRunner
{
    internal static async Task<IJobResult> RunAsync(
        JobConfig config,
        JobContext context,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes.Value));
        var ct = timeoutCts.Token;

        if (await context.Agent.EnsureInstalledAsync(context.RunProcess, ct) is InstallFailed installFailed)
        {
            return new SetupFailure($"agent install failed: {installFailed.Reason}");
        }

        var stopwatch = Stopwatch.StartNew();

        var cloneDir = Path.Combine(config.WorkDir, $"rix-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cloneDir);

        try
        {
            await context.Host.CloneAsync(cloneDir, ct);

            await using var apiServer = await LocalApiServer.StartAsync(context.Host, ct);

            var systemPrompt = BuildSystemPrompt(apiServer.BaseUrl);
            var agentResult = await RunAgentAsync(config, context, systemPrompt, cloneDir, ct);

            if (agentResult is ProcessFailure agentFailure)
            {
                stopwatch.Stop();
                return new JobFailure(
                    $"agent failed: {agentFailure.Reason}",
                    CostUsd: 0m,
                    Duration: stopwatch.Elapsed);
            }

            var costUsd = agentResult is ProcessSuccess { Output: { } resultLine }
                ? context.Agent.ParseCost(resultLine) ?? 0m
                : 0m;

            var (pendingPrs, failedBranch) =
                await BundlePendingPrsAsync(config, context, apiServer.QueuedPrRequests, cloneDir, ct);

            stopwatch.Stop();

            return failedBranch is not null
                ? new JobFailure($"git bundle failed for branch {failedBranch}", CostUsd: costUsd, stopwatch.Elapsed)
                : new JobSuccess(pendingPrs, CostUsd: costUsd, Duration: stopwatch.Elapsed);
        }
        finally
        {
            try { Directory.Delete(cloneDir, recursive: true); }
            catch (DirectoryNotFoundException) { /* already cleaned up */ }
        }
    }

    /// <summary>Runs the coding agent in the cloned repo and returns its raw process result.</summary>
    private static Task<ProcessResult> RunAgentAsync(
        JobConfig config, JobContext context, string systemPrompt, string cloneDir, CancellationToken ct)
    {
        var invocation = context.Agent.BuildInvocation(config, systemPrompt);
        return context.RunProcess(
            invocation.FileName,
            invocation.Arguments,
            cloneDir,
            invocation.EnvironmentOverrides,
            context.LogLine.Invoke,
            ct);
    }

    /// <summary>
    /// Creates a git bundle for each queued PR. Returns the bundled PRs, or — if a bundle
    /// fails — the partial list plus the branch name that failed, so the caller can map it
    /// to a <see cref="JobFailure"/> while preserving the accumulated cost.
    /// </summary>
    private static async Task<(List<PendingPr> Prs, string? FailedBranch)> BundlePendingPrsAsync(
        JobConfig config, JobContext context, IEnumerable<QueuedPr> queuedPrs, string cloneDir, CancellationToken ct)
    {
        var pendingPrs = new List<PendingPr>();
        foreach (var req in queuedPrs)
        {
            var safeName = Uri.EscapeDataString(req.Branch.Value).Replace('%', '_');
            var bundleFile = $"{safeName}.bundle";
            var bundlePath = Path.Combine(config.OutputDir, bundleFile);

            try
            {
                await context.Host.CreateBundleAsync(cloneDir, bundlePath, req.BaseBranch, req.Branch, ct);
            }
            catch (InvalidOperationException)
            {
                return (pendingPrs, req.Branch.Value);
            }

            pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
        }

        return (pendingPrs, null);
    }

    private static string BuildSystemPrompt(Uri apiBaseUrl) => $$"""
        You are `rix job`, an autonomous coding agent and part of the `rix` autonomous software factory.

        A local API is available at {{apiBaseUrl}}.

        Endpoints:
        - POST {{new Uri(apiBaseUrl, "/pr")}}     — create a pull request when satisfied with your changes

        Split your work in multiple PRs if applicable. For each:
        1. Create a branch named rix/<short-description> for your work
        2. When done, call POST {{new Uri(apiBaseUrl, "/pr")}} with JSON body:
           {"branch":"rix/<short-description>","baseBranch":"<base branch>","title":"<PR title>","body":"<PR description>"}
        """;
}
