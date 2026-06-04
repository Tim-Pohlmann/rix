using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rix.Api;
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
    CancellationToken cancellationToken,
    Action<string>? onStdoutLine = null);

internal static class JobRunner
{
    private static readonly IReadOnlyDictionary<string, string> GitEnv = new Dictionary<string, string>
    {
        ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "",
        ["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "",
    };

    internal static async Task<int> RunAsync(JobConfig config, CancellationToken cancellationToken)
    {
        var host = new GitHubRepositoryHost(config.Repo, config.ReadToken);
        return await RunAsync(config, host, (f, a, d, e, ct, cb) => ProcessWrapper.RunAsync(f, a, d, e, ct, cb), cancellationToken);
    }

    internal static async Task<int> RunAsync(
        JobConfig config,
        IRepositoryHost host,
        RunProcessAsync processRunner,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes.Value));
        var ct = timeoutCts.Token;

        var stopwatch = Stopwatch.StartNew();

        var cloneDir = Path.Combine(config.WorkDir, $"rix-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cloneDir);

        try
        {
            await host.CloneAsync(cloneDir, ct);

            await using var apiServer = await LocalApiServer.StartAsync(host, ct);

            var systemPrompt = BuildSystemPrompt(apiServer.BaseUrl);
            var tokensUsed = 0;

            var claudeResult = await processRunner(
                "claude",
                ["--output-format", "stream-json", "--print", "--append-system-prompt", systemPrompt, config.Prompt],
                cloneDir,
                new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
                },
                ct,
                line =>
                {
                    if (TryExtractTokenUsage(line, out var tokens))
                        tokensUsed = tokens;
                });

            if (!claudeResult.Succeeded)
            {
                stopwatch.Stop();
                var failure = new JobFailure(
                    claudeResult.TimedOut ? "Claude timed out" : $"Claude exited with code {claudeResult.ExitCode}",
                    TokensUsed: tokensUsed,
                    Duration: stopwatch.Elapsed);
                WriteResult(failure);
                return 1;
            }

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
                    ct,
                    null);

                if (!bundleResult.Succeeded)
                {
                    stopwatch.Stop();
                    WriteResult(new JobFailure($"git bundle failed for branch {req.Branch.Value}", TokensUsed: tokensUsed, stopwatch.Elapsed));
                    return 1;
                }

                pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
            }

            stopwatch.Stop();

            var success = new JobSuccess(pendingPrs, TokensUsed: tokensUsed, Duration: stopwatch.Elapsed);
            var resultJson = JsonSerializer.Serialize<IJobResult>(success, JobJsonContext.Default.IJobResult);
            await File.WriteAllTextAsync(Path.Combine(config.OutputDir, "result.json"), resultJson, ct);
            Console.WriteLine(resultJson);
            return 0;
        }
        finally
        {
            try { Directory.Delete(cloneDir, recursive: true); }
            catch (DirectoryNotFoundException) { /* already cleaned up */ }
        }
    }

    internal static string BuildSystemPrompt(Uri apiBaseUrl) =>
        $"The Rix API is available at {apiBaseUrl}. Use it to queue pull requests when you are done.";

    internal static bool TryExtractTokenUsage(string line, out int tokensUsed)
    {
        tokensUsed = 0;
        try
        {
            var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("usage", out var usage) &&
                usage.TryGetProperty("output_tokens", out var outputTokens))
            {
                tokensUsed = outputTokens.GetInt32();
                return true;
            }
        }
        catch (JsonException) { /* not a JSON line */ }
        return false;
    }

    private static void WriteResult(IJobResult result)
    {
        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        Console.WriteLine(json);
    }
}
