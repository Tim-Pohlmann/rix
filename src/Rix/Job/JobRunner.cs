using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rix.Api;
using Rix.Process;
using Rix.Repository;

namespace Rix.Job;

internal static class JobRunner
{
    internal static async Task<int> RunAsync(JobConfig config, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var cloneDir = Path.Combine(config.WorkDir, $"rix-{Guid.NewGuid():N}");
        var tokensUsed = 0;

        try
        {
            var claude = await ClaudeCodeInstaller.ResolveAsync(cancellationToken);
            var host = new GitHubRepositoryHost(config.Repo, config.ReadToken, config.WriteToken);

            LogInfo($"Cloning {config.Repo} into {cloneDir}...");
            await host.CloneAsync(cloneDir, cancellationToken);

            await using var apiServer = await LocalApiServer.StartAsync(host, cancellationToken);
            LogInfo($"API server started at {apiServer.BaseUrl}");

            var systemPrompt = BuildSystemPrompt(apiServer.BaseUrl);
            var claudeEnv = ProcessWrapper.BuildSanitizedEnvironment(new Dictionary<string, string>
            {
                ["CLAUDE_CODE_MAX_OUTPUT_TOKENS"] = config.MaxTokens.Value.ToString(),
            });

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(config.TimeoutMinutes.Value));

            LogInfo($"Spawning Claude Code (max tokens: {config.MaxTokens.Value}, timeout: {config.TimeoutMinutes.Value}m)...");

            var result = await ProcessWrapper.RunAsync(
                claude,
                ["-p", config.Prompt, "--output-format", "stream-json", "--max-tokens", config.MaxTokens.Value.ToString(), "--append-system-prompt", systemPrompt],
                workingDirectory: cloneDir,
                environment: claudeEnv,
                onStdoutLine: line =>
                {
                    Console.Error.WriteLine(line);
                    tokensUsed = ExtractTokenCount(line, tokensUsed);
                },
                cancellationToken: timeoutCts.Token);

            if (result.TimedOut)
            {
                WriteResult(new JobFailure(
                    $"Job timed out after {config.TimeoutMinutes.Value} minutes.",
                    tokensUsed,
                    stopwatch.Elapsed));
                return 1;
            }

            if (!result.Succeeded)
            {
                WriteResult(new JobFailure(
                    $"Claude Code exited with code {result.ExitCode}.",
                    tokensUsed,
                    stopwatch.Elapsed));
                return 1;
            }

            WriteResult(new JobSuccess(apiServer.CreatedPrs, tokensUsed, stopwatch.Elapsed));
            return 0;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            WriteResult(new JobFailure(
                $"Job timed out after {config.TimeoutMinutes.Value} minutes.",
                tokensUsed,
                stopwatch.Elapsed));
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Setup error: {ex.Message}");
            return 2;
        }
        finally
        {
            Cleanup(cloneDir);
        }
    }

    private static string BuildSystemPrompt(string apiBaseUrl) => $$"""
        You have access to a local API at {{apiBaseUrl}}.

        Available endpoints:
        - GET {{apiBaseUrl}}/health  — verify the API is reachable
        - POST {{apiBaseUrl}}/pr     — push your branch and open a pull request

        When you are satisfied with your changes:
        1. Create a branch named rix/<short-description> for your work.
        2. Call POST {{apiBaseUrl}}/pr with JSON body: { "branch": "rix/<name>", "title": "<PR title>", "body": "<PR description>" }

        The branch must match the pattern rix/* — any other name will be rejected.
        """;

    private static void WriteResult(IJobResult outcome)
    {
        var json = JsonSerializer.Serialize(outcome, JobJsonContext.Default.IJobResult);
        Console.WriteLine(json);
    }

    private static void LogInfo(string message) =>
        Console.Error.WriteLine(JsonSerializer.Serialize(
            new JobLogLine("info", message),
            JobJsonContext.Default.JobLogLine));

    private static int ExtractTokenCount(string ndjsonLine, int current)
    {
        try
        {
            using var doc = JsonDocument.Parse(ndjsonLine);
            if (doc.RootElement.TryGetProperty("usage", out var usage)
                && usage.TryGetProperty("output_tokens", out var tokens))
                return tokens.GetInt32();
        }
        catch { }
        return current;
    }

    private static void Cleanup(string cloneDir)
    {
        if (!Directory.Exists(cloneDir))
            return;
        try
        {
            Directory.Delete(cloneDir, recursive: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: cleanup of {cloneDir} failed: {ex.Message}");
        }
    }
}

internal record JobLogLine(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("message")] string Message);

[JsonSerializable(typeof(IJobResult))]
[JsonSerializable(typeof(JobSuccess))]
[JsonSerializable(typeof(JobFailure))]
[JsonSerializable(typeof(JobLogLine))]
internal partial class JobJsonContext : JsonSerializerContext { }
