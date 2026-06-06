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
        Func<CancellationToken, Task<bool>>? claudeInstaller = null)
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

        if (!await claudeInstaller(ct))
            return 2;

        var stopwatch = Stopwatch.StartNew();

        var cloneDir = Path.Combine(config.WorkDir, $"rix-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cloneDir);

        try
        {
            await host.CloneAsync(cloneDir, ct);

            await using var apiServer = await LocalApiServer.StartAsync(host, ct);

            var tokensUsed = 0;

            var claudeResult = await processRunner(
                "claude",
                ["--output-format", "stream-json", "--print", config.Prompt],
                cloneDir,
                new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
                    ["RIX_API_URL"] = apiServer.BaseUrl.ToString(),
                },
                line =>
                {
                    Console.Error.WriteLine(line);
                    TryExtractTokenUsage(line, ref tokensUsed);
                },
                ct);

            if (claudeResult is ProcessFailure claudeFailure)
            {
                stopwatch.Stop();
                var failure = new JobFailure(
                    $"Claude failed: {claudeFailure.Reason}",
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
                    null,
                    ct);

                if (bundleResult is ProcessFailure)
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

    private static void TryExtractTokenUsage(string line, ref int tokensUsed)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return;
        if (!trimmed.Contains("\"total_input_tokens\"") && !trimmed.Contains("\"total_output_tokens\"")) return;
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String || type.GetString() != "result")
                return;
            var input = root.TryGetProperty("total_input_tokens", out var i) && i.ValueKind == JsonValueKind.Number && i.TryGetInt64(out var iv) ? iv : 0L;
            var output = root.TryGetProperty("total_output_tokens", out var o) && o.ValueKind == JsonValueKind.Number && o.TryGetInt64(out var ov) ? ov : 0L;
            tokensUsed = (int)Math.Min((long)tokensUsed + input + output, int.MaxValue);
        }
        catch (JsonException) { /* non-JSON line, skip */ }
    }

    private static void WriteResult(IJobResult result)
    {
        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        Console.WriteLine(json);
    }
}
