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

internal delegate Task<ProcessResult> RunProcessAsync(
    string fileName,
    IEnumerable<string> arguments,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environmentOverrides,
    Action<string>? onStdoutLine,
    CancellationToken cancellationToken);

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

            var agentResult = await RunAgentAsync(config, context, apiServer.BaseUrl, cloneDir, ct);

            if (agentResult is ProcessFailure agentFailure)
            {
                stopwatch.Stop();
                return new JobFailure($"agent failed: {agentFailure.Reason}", CostUsd: 0m, Duration: stopwatch.Elapsed);
            }

            var costUsd = agentResult is ProcessSuccess { Output: { } resultLine }
                ? context.Agent.ParseCost(resultLine) ?? 0m
                : 0m;

            var bundles = await CreateBundlesAsync(config, context, apiServer.QueuedPrRequests, cloneDir, ct);
            stopwatch.Stop();

            return bundles switch
            {
                BundlesCreated created => new JobSuccess(created.PendingPrs, CostUsd: costUsd, Duration: stopwatch.Elapsed),
                BundleFailed failed => new JobFailure(failed.Error, CostUsd: costUsd, Duration: stopwatch.Elapsed),
                _ => throw new NotSupportedException($"Unexpected bundle outcome: {bundles.GetType()}"),
            };
        }
        finally
        {
            try { Directory.Delete(cloneDir, recursive: true); }
            catch (DirectoryNotFoundException) { /* already cleaned up */ }
        }
    }

    /// <summary>Launches the coding agent against the cloned repo, forwarding its stdout to the
    /// log sink, and returns the raw process outcome.</summary>
    private static Task<ProcessResult> RunAgentAsync(
        JobConfig config, JobContext context, Uri apiBaseUrl, string cloneDir, CancellationToken ct)
    {
        var systemPrompt = BuildSystemPrompt(apiBaseUrl);
        var invocation = context.Agent.BuildInvocation(config, systemPrompt);
        return context.RunProcess(
            invocation.FileName,
            invocation.Arguments,
            cloneDir,
            invocation.EnvironmentOverrides,
            context.LogLine.Invoke,
            ct);
    }

    /// <summary>Bundles each queued PR's commits into the output directory, stopping at the first
    /// failure. The bundle file name encodes the branch so it round-trips a path-safe slug.</summary>
    private static async Task<BundleOutcome> CreateBundlesAsync(
        JobConfig config, JobContext context, IReadOnlyList<QueuedPr> requests, string cloneDir, CancellationToken ct)
    {
        var pendingPrs = new List<PendingPr>();
        foreach (var req in requests)
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
                return new BundleFailed($"git bundle failed for branch {req.Branch.Value}");
            }

            pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
        }

        return new BundlesCreated(pendingPrs);
    }

    private abstract record BundleOutcome;
    private sealed record BundlesCreated(IReadOnlyList<PendingPr> PendingPrs) : BundleOutcome;
    private sealed record BundleFailed(string Error) : BundleOutcome;

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
