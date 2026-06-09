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
    private static readonly RunProcessAsync DefaultRunProcess =
        (fileName, arguments, workingDirectory, environmentOverrides, onStdoutLine, token) =>
            ProcessWrapper.RunAsync(fileName, arguments,
                workingDirectory: workingDirectory,
                environmentOverrides: environmentOverrides,
                cancellationToken: token,
                onStdoutLine: onStdoutLine);

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
        var errors = config.ValidationErrors
            .Concat(config.FilesystemValidationErrors(Directory.Exists))
            .ToList();

        if (errors.Count > 0)
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
    /// JSON to stdout, persists <c>result.json</c> on success, and maps the outcome to an exit code.
    /// </summary>
    internal static async Task<int> ExecuteJobAsync(
        JobConfig config,
        CancellationToken cancellationToken,
        IRepositoryHost? host = null,
        RunProcessAsync? processRunner = null,
        Func<CancellationToken, Task<InstallResult>>? claudeInstaller = null)
    {
        var runProcess = processRunner ?? DefaultRunProcess;
        var installClaude = claudeInstaller ?? (token => ClaudeInstaller.EnsureInstalledAsync(token,
            runProcess: (fileName, args, t) => runProcess(fileName, args, Path.GetTempPath(), null, null, t)));
        var effects = new JobEffects(
            Host: host ?? new GitHubRepositoryHost(config.Repo, config.ReadToken),
            RunProcess: runProcess,
            InstallClaude: installClaude,
            LogLine: Console.Error.WriteLine);

        var outcome = await JobRunner.RunAsync(config, effects, cancellationToken);

        var json = JsonSerializer.Serialize(outcome.Result, JobJsonContext.Default.IJobResult);
        Console.WriteLine(json);

        if (outcome.Result is JobSuccess)
            await File.WriteAllTextAsync(Path.Combine(config.OutputDir, "result.json"), json, cancellationToken);

        return outcome.ExitCode;
    }
}
