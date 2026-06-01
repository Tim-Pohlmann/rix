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

internal static class JobRunner
{
    internal static async Task<int> RunAsync(JobConfig config, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var host = new GitHubRepositoryHost(config.Repo, config.ReadToken);

        var cloneDir = Path.Combine(config.WorkDir, $"rix-clone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cloneDir);

        try
        {
            await host.CloneAsync(cloneDir, cancellationToken);

            await using var apiServer = await LocalApiServer.StartAsync(host, cancellationToken);

            var claudeResult = await ProcessWrapper.RunAsync(
                "claude", ["--output-format", "stream-json", "--print", config.Prompt],
                workingDirectory: cloneDir,
                environmentOverrides: new Dictionary<string, string>
                {
                    ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
                    ["RIX_API_URL"] = apiServer.BaseUrl.ToString(),
                },
                cancellationToken: cancellationToken);

            stopwatch.Stop();

            if (!claudeResult.Succeeded)
            {
                var failure = new JobFailure(
                    claudeResult.TimedOut ? "Claude timed out" : $"Claude exited with code {claudeResult.ExitCode}",
                    TokensUsed: 0,
                    Duration: stopwatch.Elapsed);
                WriteResult(failure);
                return 1;
            }

            var pendingPrs = new List<PendingPr>();
            foreach (var req in apiServer.PendingPrRequests)
            {
                var safeName = req.Branch.Value.Replace('/', '-');
                var bundleFile = $"{safeName}.bundle";
                var bundlePath = Path.Combine(config.OutputDir, bundleFile);

                var bundleResult = await ProcessWrapper.RunAsync(
                    "git", ["bundle", "create", bundlePath, $"HEAD..{req.Branch.Value}"],
                    workingDirectory: cloneDir,
                    environmentOverrides: new Dictionary<string, string>
                    {
                        ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "",
                        ["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "",
                    },
                    cancellationToken: CancellationToken.None);

                if (!bundleResult.Succeeded)
                    throw new InvalidOperationException($"git bundle failed for branch {req.Branch.Value}");

                pendingPrs.Add(new PendingPr(req.Branch, req.BaseBranch, req.Title, req.Body, bundleFile));
            }

            var success = new JobSuccess(pendingPrs, TokensUsed: 0, Duration: stopwatch.Elapsed);

            var resultJson = JsonSerializer.Serialize<IJobResult>(success, JobJsonContext.Default.IJobResult);
            await File.WriteAllTextAsync(Path.Combine(config.OutputDir, "result.json"), resultJson, CancellationToken.None);

            WriteResult(success);
            return 0;
        }
        finally
        {
            if (Directory.Exists(cloneDir))
                Directory.Delete(cloneDir, recursive: true);
        }
    }

    private static void WriteResult(IJobResult result)
    {
        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        Console.WriteLine(json);
    }
}
