using System.Diagnostics;
using System.Text.Json.Serialization;
using Rix.Api;
using Rix.Claude;
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
    private static readonly IReadOnlyDictionary<string, string> GitEnv = new Dictionary<string, string>
    {
        ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "",
        ["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "",
    };

    internal static async Task<IJobResult> RunAsync(
        JobConfig config,
        JobContext context,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes.Value));
        var ct = timeoutCts.Token;

        if (await context.InstallClaude(ct) is InstallFailed installFailed)
        {
            return new SetupFailure($"Claude install failed: {installFailed.Reason}");
        }

        var stopwatch = Stopwatch.StartNew();

        var cloneDir = Path.Combine(config.WorkDir, $"rix-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cloneDir);

        try
        {
            await context.Host.CloneAsync(cloneDir, ct);

            await using var apiServer = await LocalApiServer.StartAsync(context.Host, ct);

            var systemPrompt = BuildSystemPrompt(apiServer.BaseUrl);

            var claudeResult = await context.RunProcess(
                "claude",
                ["--output-format", "stream-json", "--print", config.Prompt, "--append-system-prompt", systemPrompt],
                cloneDir,
                new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
                },
                context.LogLine,
                ct);

            if (claudeResult is ProcessFailure claudeFailure)
            {
                stopwatch.Stop();
                var failure = new JobFailure(
                    $"Claude failed: {claudeFailure.Reason}",
                    CostUsd: 0m,
                    Duration: stopwatch.Elapsed);
                return failure;
            }

            var costUsd = claudeResult is ProcessSuccess { Output: { } resultLine }
                ? JobCost.FromResultLine(resultLine) ?? 0m
                : 0m;

            var pendingPrs = new List<PendingPr>();
            foreach (var req in apiServer.QueuedPrRequests)
            {
                var safeName = Uri.EscapeDataString(req.Branch.Value).Replace('%', '_');
                var bundleFile = $"{safeName}.bundle";
                var bundlePath = Path.Combine(config.OutputDir, bundleFile);

                var bundleResult = await context.RunProcess(
                    "git",
                    ["bundle", "create", bundlePath, $"{req.BaseBranch.Value}..{req.Branch.Value}"],
                    cloneDir,
                    GitEnv,
                    null,
                    ct);

                if (bundleResult is ProcessFailure)
                {
                    stopwatch.Stop();
                    return new JobFailure($"git bundle failed for branch {req.Branch.Value}", CostUsd: costUsd, stopwatch.Elapsed);
                }

                pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
            }

            stopwatch.Stop();

            return new JobSuccess(pendingPrs, CostUsd: costUsd, Duration: stopwatch.Elapsed);
        }
        finally
        {
            try { Directory.Delete(cloneDir, recursive: true); }
            catch (DirectoryNotFoundException) { /* already cleaned up */ }
        }
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
