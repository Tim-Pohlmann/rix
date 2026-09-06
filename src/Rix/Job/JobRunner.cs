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

        await using var apiServer = await LocalApiServer.StartAsync
        (
            context.Host, cloneDir.Path, ct, context.LogLine.Invoke,
            allowedPushBranches: config.AllowedPushBranches
        );

        var systemPrompt = BuildSystemPrompt(apiServer.BaseUrl, config.AllowedPushBranches);
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

        var delivery = await BundlePendingAsync(config, context, apiServer.GetQueuedPrRequests(), apiServer.GetQueuedPushRequests(), cloneDir.Path, ct);

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
            WithApiKey(invocation.EnvironmentOverrides, config.Agent),
            ForwardLine,
            ct
        );

        void ForwardLine(string line)
        {
            context.LogLine(line);
            if (context.Agent.ParseTranscriptLine(line) is { } transcriptLine)
                context.TranscriptLine(transcriptLine);
        }
    }

    /// <summary>Adds the resolved agent credential (see <see cref="AgentCredential.ResolveEnvName"/>)
    /// to the agent's own environment overrides, under whichever single env var name it was resolved
    /// to. This is the only place the credential is added to any process's environment — it lands
    /// directly in the spawned agent CLI's environment table, never in rix's own.</summary>
    private static IReadOnlyDictionary<string, string> WithApiKey
    (
        IReadOnlyDictionary<string, string> environmentOverrides, AgentConfig agent
    )
    {
        if (agent.ApiKey is not { } apiKey)
            return environmentOverrides;

        var withApiKey = new Dictionary<string, string>(environmentOverrides) { [agent.ApiKeyEnv!] = apiKey };
        return withApiKey;
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
            var request = new BundleRequest(req.Branch, req.BaseBranch, "PR");
            switch (await BundleBranchAsync(config, context, cloneDir, request, seenBranches, ct))
            {
                case BundleSkipped:
                    continue;
                case BundleFailed:
                    return new DeliveryFailed(req.Branch.Value);
                case Bundled(var bundleFile):
                    pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
                    break;
            }
        }

        foreach (var push in queuedPushes)
        {
            var request = new BundleRequest(push.Branch, push.BaseBranch, "push");
            switch (await BundleBranchAsync(config, context, cloneDir, request, seenBranches, ct))
            {
                case BundleSkipped:
                    continue;
                case BundleFailed:
                    return new DeliveryFailed(push.Branch.Value);
                case Bundled(var bundleFile):
                    pendingPushes.Add(new PendingPush(push.Branch, push.BaseBranch, bundleFile));
                    break;
            }
        }

        return new Delivered(pendingPrs, pendingPushes);
    }

    /// <summary>A queued branch to bundle, stripped down to what <see cref="BundleBranchAsync"/>
    /// needs: identity (<paramref name="Branch"/>/<paramref name="BaseBranch"/>) plus
    /// <paramref name="Kind"/> ("PR" or "push") for the skip log line.</summary>
    private readonly record struct BundleRequest(RixBranchName Branch, BranchName BaseBranch, string Kind);

    /// <summary>Dedups <paramref name="request"/>'s branch against <paramref name="seenBranches"/>
    /// (shared across the PR and push queues, so the same branch is never bundled twice in one run)
    /// and, if new, creates its git bundle.</summary>
    private static async Task<BundleOutcome> BundleBranchAsync
    (
        JobConfig config,
        JobContext context,
        string cloneDir,
        BundleRequest request,
        HashSet<string> seenBranches,
        CancellationToken ct
    )
    {
        // Two requests queued in one run can name the same branch; their bundle file names would
        // collide and the second would overwrite the first. Keep the first and skip the rest.
        if (!seenBranches.Add(request.Branch.Value))
        {
            context.LogLine($"skipping duplicate queued {request.Kind} for branch {request.Branch.Value}");
            return new BundleSkipped();
        }

        var safeName = Uri.EscapeDataString(request.Branch.Value).Replace('%', '_');
        var bundleFile = $"{safeName}.bundle";
        var bundlePath = Path.Combine(config.OutputDir.Value, bundleFile);

        try
        {
            await context.Host.CreateBundleAsync(cloneDir, bundlePath, request.BaseBranch, request.Branch, ct);
        }
        catch (InvalidOperationException)
        {
            return new BundleFailed();
        }

        return new Bundled(bundleFile);
    }

    /// <summary>The result of bundling one queued branch: bundled, skipped as a same-run duplicate,
    /// or failed.</summary>
    private abstract record BundleOutcome
    {
        private protected BundleOutcome() { }
    }
    private sealed record Bundled(string BundleFile) : BundleOutcome;
    private sealed record BundleSkipped : BundleOutcome;
    private sealed record BundleFailed : BundleOutcome;

    /// <summary>The result of bundling the queued requests: either all were turned into
    /// deliverables, or one failed (identified by its branch).</summary>
    private abstract record DeliveryOutcome
    {
        private protected DeliveryOutcome() { }
    }
    private sealed record Delivered(IReadOnlyList<PendingPr> PendingPrs, IReadOnlyList<PendingPush> PendingPushes) : DeliveryOutcome;
    private sealed record DeliveryFailed(string Branch) : DeliveryOutcome;

    private static string BuildSystemPrompt(Uri apiBaseUrl, IReadOnlyList<RixBranchName> allowedPushBranches)
    {
        var prUri = new Uri(apiBaseUrl, "/pr");
        var pushUri = new Uri(apiBaseUrl, "/push");
        return $$"""
        You are `rix job`, an autonomous coding agent and part of the `rix` autonomous software factory.

        A local API is available at {{apiBaseUrl}}.

        Endpoints:
        - POST   {{prUri}}     — create a pull request when satisfied with your changes
        - GET    {{prUri}}     — list your queued pull requests
        - DELETE {{prUri}}     — cancel a queued pull request (body: {"branch":"rix/<branch>"})
        - POST   {{pushUri}}   — push new commits onto a branch that already exists on the remote
        - GET    {{pushUri}}   — list your queued pushes
        - DELETE {{pushUri}}   — cancel a queued push (body: {"branch":"rix/<branch>"})

        Split your work in multiple PRs if applicable. For each:
        1. Create a branch named rix/<short-description> for your work
        2. When done, call POST {{prUri}} with JSON body:
           {"branch":"rix/<short-description>","baseBranch":"<base branch>","title":"<PR title>","body":"<PR description>"}

        You can list what you have already queued with GET, and cancel a queued request with DELETE
        on the same path before the job ends (handy when you change your mind about a branch).

        To add commits to a branch that already exists on the remote (e.g. resuming a previous run),
        commit them locally on that branch, then call POST {{pushUri}} with JSON
        body:
           {"branch":"rix/<existing-branch>","baseBranch":"<base branch>"}

        {{AllowedPushBranchesPrompt(allowedPushBranches)}}
        """;
    }

    /// <summary>Renders the push restriction as instructions the agent must follow, so it learns
    /// what /push will accept from the prompt instead of only from rejected requests. /push denies
    /// every branch unless the operator explicitly allowed some, so the empty case still needs a
    /// sentence — silence there would read as "unrestricted" to the agent.</summary>
    private static string AllowedPushBranchesPrompt(IReadOnlyList<RixBranchName> allowedPushBranches)
    => allowedPushBranches.Count switch
    {
        0 => "This job has not allowed any push branches, so /push will reject every request; use /pr for all changes.",
        _ => $"This job's push endpoint is restricted: you may only push onto the branches: {string.Join(", ", allowedPushBranches.Select(b => b.Value))}.",
    };
}
