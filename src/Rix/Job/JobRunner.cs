using Rix.Agents;
using Rix.Api;
using Rix.Process;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Rix.Job;

[JsonSerializable(typeof(IJobResult))]
[JsonSerializable(typeof(JobSuccess))]
[JsonSerializable(typeof(JobFailure))]
[JsonSerializable(typeof(SetupFailure))]
[JsonSerializable(typeof(PendingPr))]
[JsonSerializable(typeof(PendingPush))]
internal partial class JobJsonContext : JsonSerializerContext { }

internal static class JobRunner
{
    internal static async Task<IJobResult> RunAsync
    (
        JobConfig config,
        JobContext context,
        CancellationToken cancellationToken
    )
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes.Value));
        var ct = timeoutCts.Token;

        if (await context.Agent.EnsureInstalledAsync(context.RunProcess, ct) is InstallFailed installFailed)
        {
            return new SetupFailure($"agent install failed: {installFailed.Reason}");
        }

        var stopwatch = Stopwatch.StartNew();

        using var cloneDir = TempDirectory.Create(config.WorkDir.Value, "rix-clone");

        try
        {
            await context.Host.CloneAsync(cloneDir.Path, ct);
            // Set the commit identity before the agent starts, so it can commit without guessing
            // author metadata.
            await context.Host.ConfigureGitAsync(cloneDir.Path, ct);
        }
        catch (InvalidOperationException ex)
        {
            return new SetupFailure(ex.Message);
        }

        await using var apiServer = await LocalApiServer.StartAsync(context.Host, cloneDir.Path, ct, context.LogLine.Invoke);

        var systemPrompt = BuildSystemPrompt(apiServer.BaseUrl);
        var agentResult = await RunAgentAsync(config, context, systemPrompt, cloneDir.Path, ct);

        if (agentResult is ProcessFailure agentFailure)
        {
            stopwatch.Stop();
            var detail = agentFailure.Reason;
            if (!string.IsNullOrWhiteSpace(agentFailure.Diagnostic))
                detail = $"{agentFailure.Reason}: {agentFailure.Diagnostic}";
            return new JobFailure
            (
                $"agent failed: {detail}",
                CostUsd: 0m,
                Duration: stopwatch.Elapsed
            );
        }

        var costUsd = agentResult switch
        {
            ProcessSuccess { Output: { } resultLine } => context.Agent.ParseCost(resultLine) ?? 0m,
            _ => 0m,
        };

        var delivery = await BundlePendingAsync(config, context, apiServer.QueuedPrRequests, apiServer.QueuedPushRequests, cloneDir.Path, ct);

        stopwatch.Stop();

        return delivery switch
        {
            Delivered { PendingPrs: var pendingPrs, PendingPushes: var pendingPushes }
                => new JobSuccess(pendingPrs, pendingPushes, CostUsd: costUsd, Duration: stopwatch.Elapsed),
            DeliveryFailed { Branch: var branch }
                => new JobFailure($"git bundle failed for branch {branch}", CostUsd: costUsd, stopwatch.Elapsed),
            _ => throw new NotSupportedException($"Unexpected delivery outcome: {delivery.GetType()}"),
        };
    }

    /// <summary>Runs the coding agent in the cloned repo and returns its raw process result.</summary>
    private static Task<ProcessResult> RunAgentAsync
    (
        JobConfig config, JobContext context, string systemPrompt, string cloneDir, CancellationToken ct
    )
    {
        var invocation = context.Agent.BuildInvocation(config, systemPrompt);
        return context.RunProcess
        (
            invocation.FileName,
            invocation.Arguments,
            cloneDir,
            invocation.EnvironmentOverrides,
            context.LogLine.Invoke,
            ct
        );
    }

    /// <summary>
    /// Creates a git bundle for each queued PR and each queued push. Returns
    /// <see cref="Delivered"/> with the bundled PRs and pushes, or — if a bundle fails —
    /// <see cref="DeliveryFailed"/> naming the branch, so the caller can map it to a
    /// <see cref="JobFailure"/> while preserving the accumulated cost. PRs and pushes share one
    /// dedup set: the same branch must not be bundled twice in a single run, no matter which
    /// queue it was queued from.
    /// </summary>
    private static async Task<DeliveryOutcome> BundlePendingAsync
    (
        JobConfig config,
        JobContext context,
        IEnumerable<QueuedPr> queuedPrs,
        IEnumerable<QueuedPush> queuedPushes,
        string cloneDir,
        CancellationToken ct
    )
    {
        var pendingPrs = new List<PendingPr>();
        var pendingPushes = new List<PendingPush>();
        var seenBranches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var req in queuedPrs)
        {
            // Two PRs queued in one run can name the same branch; their bundle file names would
            // collide and the second would overwrite the first. Keep the first and skip the rest.
            if (!seenBranches.Add(req.Branch.Value))
            {
                context.LogLine($"skipping duplicate queued PR for branch {req.Branch.Value}");
                continue;
            }

            var safeName = Uri.EscapeDataString(req.Branch.Value).Replace('%', '_');
            var bundleFile = $"{safeName}.bundle";
            var bundlePath = Path.Combine(config.OutputDir.Value, bundleFile);

            try
            {
                await context.Host.CreateBundleAsync(cloneDir, bundlePath, req.BaseBranch, req.Branch, ct);
            }
            catch (InvalidOperationException)
            {
                return new DeliveryFailed(req.Branch.Value);
            }

            pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
        }

        foreach (var push in queuedPushes)
        {
            // A push for a branch that was already queued as a PR (or vice versa) must not produce
            // a second bundle with a colliding file name, so the two queues share one dedup set.
            if (!seenBranches.Add(push.Branch.Value))
            {
                context.LogLine($"skipping duplicate queued push for branch {push.Branch.Value}");
                continue;
            }

            var safeName = Uri.EscapeDataString(push.Branch.Value).Replace('%', '_');
            var bundleFile = $"{safeName}.bundle";
            var bundlePath = Path.Combine(config.OutputDir.Value, bundleFile);

            try
            {
                await context.Host.CreateBundleAsync(cloneDir, bundlePath, push.BaseBranch, push.Branch, ct);
            }
            catch (InvalidOperationException)
            {
                return new DeliveryFailed(push.Branch.Value);
            }

            pendingPushes.Add(new PendingPush(push.Branch, push.BaseBranch, bundleFile));
        }

        return new Delivered(pendingPrs, pendingPushes);
    }

    /// <summary>The result of bundling the queued requests: either all were turned into
    /// deliverables, or one failed (identified by its branch).</summary>
    private abstract record DeliveryOutcome
    {
        private protected DeliveryOutcome() { }
    }
    private sealed record Delivered(IReadOnlyList<PendingPr> PendingPrs, IReadOnlyList<PendingPush> PendingPushes) : DeliveryOutcome;
    private sealed record DeliveryFailed(string Branch) : DeliveryOutcome;

    private static string BuildSystemPrompt(Uri apiBaseUrl) => $$"""
        You are `rix job`, an autonomous coding agent and part of the `rix` autonomous software factory.

        A local API is available at {{apiBaseUrl}}.

        Endpoints:
        - POST {{new Uri(apiBaseUrl, "/pr")}}     — create a pull request when satisfied with your changes
        - POST {{new Uri(apiBaseUrl, "/push")}}   — push new commits onto a branch that already exists on the remote

        Split your work in multiple PRs if applicable. For each:
        1. Create a branch named rix/<short-description> for your work
        2. When done, call POST {{new Uri(apiBaseUrl, "/pr")}} with JSON body:
           {"branch":"rix/<short-description>","baseBranch":"<base branch>","title":"<PR title>","body":"<PR description>"}

        To add commits to a branch that already exists on the remote (e.g. continuing work from a
        previous run), commit them locally on that branch, then call POST {{new Uri(apiBaseUrl, "/push")}}
        with JSON body:
           {"branch":"rix/<existing-branch>","baseBranch":"<base branch>"}
        """;
}
