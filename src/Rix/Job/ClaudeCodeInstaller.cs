using Rix.Process;

namespace Rix.Job;

internal static class ClaudeCodeInstaller
{
    private const string PinnedVersion = "1.0.20";
    private const string MinimumVersion = "1.0.0";

    internal static async Task<string> ResolveAsync(CancellationToken cancellationToken)
    {
        var existing = await TryGetInstalledVersionAsync(cancellationToken);
        if (existing is not null && MeetsMinimumVersion(existing))
            return "claude";

        await InstallAsync(cancellationToken);
        return "claude";
    }

    private static async Task<string?> TryGetInstalledVersionAsync(CancellationToken cancellationToken)
    {
        string? version = null;
        try
        {
            var result = await ProcessWrapper.RunAsync(
                "claude", ["--version"],
                workingDirectory: Path.GetTempPath(),
                environment: ProcessWrapper.BuildSanitizedEnvironment(),
                onStdoutLine: line => version = line.Trim(),
                cancellationToken: cancellationToken);

            return result.Succeeded ? version : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool MeetsMinimumVersion(string versionOutput)
    {
        var raw = versionOutput.Split(' ').LastOrDefault() ?? versionOutput;
        return Version.TryParse(raw.TrimStart('v'), out var parsed)
            && Version.TryParse(MinimumVersion, out var min)
            && parsed >= min;
    }

    private static async Task InstallAsync(CancellationToken cancellationToken)
    {
        var result = await ProcessWrapper.RunAsync(
            "npm", ["install", "-g", $"@anthropic-ai/claude-code@{PinnedVersion}"],
            workingDirectory: Path.GetTempPath(),
            environment: ProcessWrapper.BuildSanitizedEnvironment(),
            onStdoutLine: line => Console.Error.WriteLine(line),
            cancellationToken: cancellationToken);

        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to install Claude Code via npm (exit code {result.ExitCode}). " +
                "Ensure npm is available on PATH.");
    }
}
