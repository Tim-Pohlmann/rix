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
    CancellationToken cancellationToken);

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
        return await RunAsync(config, host, (f, a, d, e, ct) => ProcessWrapper.RunAsync(f, a, d, e, ct), cancellationToken);
    }

    internal static async Task<int> RunAsync(
        JobConfig config,
        IRepositoryHost host,
        RunProcessAsync processRunner,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var cloneDir = Path.Combine(config.WorkDir, $"rix-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cloneDir);

        try
        {
            await host.CloneAsync(cloneDir, cancellationToken);

            await using var apiServer = await LocalApiServer.StartAsync(host, cancellationToken);

            var claudeResult = await processRunner(
                "claude",
                ["--output-format", "stream-json", "--print", config.Prompt],
                cloneDir,
                new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
                    ["RIX_API_URL"] = apiServer.BaseUrl.ToString(),
                },
                cancellationToken);

            if (!claudeResult.Succeeded)
            {
                stopwatch.Stop();
                var failure = new JobFailure(
                    claudeResult.TimedOut ? "Claude timed out" : $"Claude exited with code {claudeResult.ExitCode}",
                    TokensUsed: 0,
                    Duration: stopwatch.Elapsed);
                WriteResult(failure);
                return 1;
            }

            var pendingPrs = new List<PendingPr>();
            foreach (var req in apiServer.QueuedPrRequests)
            {
                var safeName = req.Branch.Value.Replace('/', '-');
                var bundleFile = $"{safeName}.bundle";
                var bundlePath = Path.Combine(config.OutputDir, bundleFile);

                var bundleResult = await processRunner(
                    "git",
                    ["bundle", "create", bundlePath, $"{req.BaseBranch.Value}..{req.Branch.Value}"],
                    cloneDir,
                    GitEnv,
                    CancellationToken.None);

                if (!bundleResult.Succeeded)
                {
                    stopwatch.Stop();
                    WriteResult(new JobFailure($"git bundle failed for branch {req.Branch.Value}", TokensUsed: 0, stopwatch.Elapsed));
                    return 1;
                }

                pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, BundleFile: bundleFile));
            }

            stopwatch.Stop();

            var success = new JobSuccess(pendingPrs, TokensUsed: 0, Duration: stopwatch.Elapsed);
            var resultJson = JsonSerializer.Serialize<IJobResult>(success, JobJsonContext.Default.IJobResult);
            await File.WriteAllTextAsync(Path.Combine(config.OutputDir, "result.json"), resultJson, cancellationToken);
            Console.WriteLine(resultJson);
            return 0;
        }
        finally
        {
            try { Directory.Delete(cloneDir, recursive: true); }
            catch (DirectoryNotFoundException) { /* already cleaned up */ }
        }
    }

    private static void WriteResult(IJobResult result)
    {
        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        Console.WriteLine(json);
    }
}
