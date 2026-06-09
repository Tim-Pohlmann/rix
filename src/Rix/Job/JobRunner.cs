using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rix.Api;
using Rix.Claude;
using Rix.Process;
using Rix.Repository;

namespace Rix.Job;

[JsonSerializable(typeof(IJobResult))]
[JsonSerializable(typeof(JobSuccess))]
[JsonSerializable(typeof(JobFailure))]
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

    internal static async Task<int> RunAsync(
        JobConfig config,
        CancellationToken cancellationToken,
        IRepositoryHost? host = null,
        RunProcessAsync? processRunner = null,
        Func<CancellationToken, Task<InstallResult>>? claudeInstaller = null)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes.Value));
        var ct = timeoutCts.Token;

        host ??= new GitHubRepositoryHost(config.Repo, config.ReadToken);
        processRunner ??= (fileName, arguments, workingDirectory, environmentOverrides, onStdoutLine, token) =>
            ProcessWrapper.RunAsync(fileName, arguments,
                workingDirectory: workingDirectory,
                environmentOverrides: environmentOverrides,
                cancellationToken: token,
                onStdoutLine: onStdoutLine);
        claudeInstaller ??= token => ClaudeInstaller.EnsureInstalledAsync(token,
            runProcess: (fileName, args, t) => processRunner(fileName, args, Path.GetTempPath(), null, null, t));

        if (await claudeInstaller(ct) is InstallFailed installFailed)
        {
            WriteResult(new JobFailure($"Claude install failed: {installFailed.Reason}", CostUsd: 0m, Duration: TimeSpan.Zero));
            return ExitCodes.SetupFailed;
        }

        var stopwatch = Stopwatch.StartNew();

        var cloneDir = Path.Combine(config.WorkDir, $"rix-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cloneDir);

        try
        {
            await host.CloneAsync(cloneDir, ct);

            await using var apiServer = await LocalApiServer.StartAsync(host, ct);

            var systemPrompt = BuildSystemPrompt(apiServer.BaseUrl);

            var claudeResult = await processRunner(
                "claude",
                ["--output-format", "stream-json", "--print", config.Prompt, "--append-system-prompt", systemPrompt],
                cloneDir,
                new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
                },
                Console.Error.WriteLine,
                ct);

            if (claudeResult is ProcessFailure claudeFailure)
            {
                stopwatch.Stop();
                var failure = new JobFailure(
                    $"Claude failed: {claudeFailure.Reason}",
                    CostUsd: 0m,
                    Duration: stopwatch.Elapsed);
                WriteResult(failure);
                return ExitCodes.JobFailed;
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

                var bundleResult = await processRunner(
                    "git",
                    ["bundle", "create", bundlePath, $"{req.BaseBranch.Value}..{req.Branch.Value}"],
                    cloneDir,
                    GitEnv,
                    null,
                    ct);

                if (bundleResult is ProcessFailure)
                {
                    stopwatch.Stop();
                    WriteResult(new JobFailure($"git bundle failed for branch {req.Branch.Value}", CostUsd: costUsd, stopwatch.Elapsed));
                    return ExitCodes.JobFailed;
                }

                pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
            }

            stopwatch.Stop();

            var success = new JobSuccess(pendingPrs, CostUsd: costUsd, Duration: stopwatch.Elapsed);
            var resultJson = JsonSerializer.Serialize<IJobResult>(success, JobJsonContext.Default.IJobResult);
            await File.WriteAllTextAsync(Path.Combine(config.OutputDir, "result.json"), resultJson, ct);
            Console.WriteLine(resultJson);
            return ExitCodes.Success;
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

    private static void WriteResult(IJobResult result)
    {
        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        Console.WriteLine(json);
    }
}
