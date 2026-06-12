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
        context.FileSystem.CreateDirectory(cloneDir);

        try
        {
            await context.Host.CloneAsync(cloneDir, ct);

            await using var apiServer = await LocalApiServer.StartAsync(context.Host, ct);

            var systemPrompt = BuildSystemPrompt(apiServer.BaseUrl);
            var invocation = context.Agent.BuildInvocation(config, systemPrompt);

            var agentResult = await context.RunProcess(
                invocation.FileName,
                invocation.Arguments,
                cloneDir,
                invocation.EnvironmentOverrides,
                context.LogLine.Invoke,
                ct);

            if (agentResult is ProcessFailure agentFailure)
            {
                stopwatch.Stop();
                var failure = new JobFailure(
                    $"agent failed: {agentFailure.Reason}",
                    CostUsd: 0m,
                    Duration: stopwatch.Elapsed);
                return failure;
            }

            var costUsd = agentResult is ProcessSuccess { Output: { } resultLine }
                ? context.Agent.ParseCost(resultLine) ?? 0m
                : 0m;

            var delivery = await DeliverQueuedPrsAsync(apiServer.QueuedPrRequests, config, context, cloneDir, ct);

            stopwatch.Stop();

            return delivery switch
            {
                Delivered { PendingPrs: var pendingPrs } =>
                    new JobSuccess(pendingPrs, CostUsd: costUsd, Duration: stopwatch.Elapsed),
                DeliveryFailed { Branch: var branch } =>
                    new JobFailure($"git bundle failed for branch {branch}", CostUsd: costUsd, stopwatch.Elapsed),
                _ => throw new NotSupportedException($"Unexpected delivery outcome: {delivery.GetType()}"),
            };
        }
        finally
        {
            context.FileSystem.DeleteDirectory(cloneDir);
        }
    }

    /// <summary>Turns the agent's queued PR requests into deliverables. Today every PR is
    /// delivered as a git bundle written to <see cref="JobConfig.OutputDir"/>; isolating this here
    /// keeps <see cref="RunAsync"/> a readable orchestration and leaves room for a second delivery
    /// channel (e.g. direct push) to become an injected strategy later.</summary>
    private static async Task<DeliveryOutcome> DeliverQueuedPrsAsync(
        IReadOnlyList<QueuedPr> queuedPrs,
        JobConfig config,
        JobContext context,
        string cloneDir,
        CancellationToken cancellationToken)
    {
        var pendingPrs = new List<PendingPr>();
        foreach (var req in queuedPrs)
        {
            var safeName = Uri.EscapeDataString(req.Branch.Value).Replace('%', '_');
            var bundleFile = $"{safeName}.bundle";
            var bundlePath = Path.Combine(config.OutputDir, bundleFile);

            var bundleResult = await context.RunProcess(
                "git",
                ["bundle", "create", bundlePath, $"{req.BaseBranch.Value}..{req.Branch.Value}"],
                cloneDir,
                ProcessEnv.Inherited,
                null,
                cancellationToken);

            if (bundleResult is ProcessFailure)
                return new DeliveryFailed(req.Branch.Value);

            pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
        }

        return new Delivered(pendingPrs);
    }

    /// <summary>The result of delivering the queued PRs: either all were turned into deliverables,
    /// or one failed (identified by its branch).</summary>
    private abstract record DeliveryOutcome;
    private sealed record Delivered(IReadOnlyList<PendingPr> PendingPrs) : DeliveryOutcome;
    private sealed record DeliveryFailed(string Branch) : DeliveryOutcome;

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
