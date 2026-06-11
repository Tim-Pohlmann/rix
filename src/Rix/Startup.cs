using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text.Json;
using Rix.Claude;
using Rix.Cli;
using Rix.Job;
using Rix.Process;
using Rix.Repository;

namespace Rix;

internal static class Startup
{
    internal static readonly RunProcessAsync DefaultRunProcess =
        (fileName, arguments, workingDirectory, environmentOverrides, onStdoutLine, token) =>
            ProcessWrapper.RunAsync(fileName, arguments,
                workingDirectory: workingDirectory,
                environmentOverrides: environmentOverrides,
                cancellationToken: token,
                onStdoutLine: onStdoutLine);

    /// <summary>The production <see cref="JobContext"/>: real GitHub host, process runner,
    /// Claude installer, and stderr log sink, all wired from <paramref name="config"/>.</summary>
    internal static JobContext DefaultContext(JobConfig config) =>
        new(
            Host: new GitHubRepositoryHost(config.Repo, config.ReadToken),
            RunProcess: DefaultRunProcess,
            InstallClaude: token => ClaudeInstaller.EnsureInstalledAsync(token,
                runProcess: (fileName, args, t) => DefaultRunProcess(fileName, args, Path.GetTempPath(), null, null, t)),
            LogLine: Console.Error.WriteLine);

    internal static async Task<int> RunAsync(string[] args)
    {
        var rootCommand = new RootCommand("RIX - AI-powered code automation");

        rootCommand.AddCommand(JobCommand.Build(RunJobAsync));

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }

    private static async Task<int> RunJobAsync(JobConfig config)
    {
        if (config.ValidationErrors is { Count: > 0 } errors)
        {
            foreach (var error in errors)
                Console.Error.WriteLine($"error: {error}");
            return ExitCodes.SetupFailed;
        }

        return await ExecuteJobAsync(config, CancellationToken.None);
    }

    /// <summary>
    /// Imperative shell around the pure-ish <see cref="JobRunner.RunAsync"/> core: runs the job,
    /// then performs all output effects — forwards Claude's stream to stderr, writes the result
    /// JSON to stdout, persists <c>result.json</c> on success, and maps the result to an exit code.
    /// </summary>
    internal static async Task<int> ExecuteJobAsync(
        JobConfig config,
        CancellationToken cancellationToken,
        JobContext? context = null)
    {
        context ??= DefaultContext(config);

        var result = await JobRunner.RunAsync(config, context, cancellationToken);

        var json = JsonSerializer.Serialize(result, JobJsonContext.Default.IJobResult);
        Console.WriteLine(json);

        if (result is JobSuccess)
            await File.WriteAllTextAsync(Path.Combine(config.OutputDir, "result.json"), json, cancellationToken);

        return result switch
        {
            JobSuccess => ExitCodes.Success,
            SetupFailure => ExitCodes.SetupFailed,
            JobFailure => ExitCodes.JobFailed,
            _ => throw new NotSupportedException($"Unexpected job result type: {result.GetType()}"),
        };
    }
}
