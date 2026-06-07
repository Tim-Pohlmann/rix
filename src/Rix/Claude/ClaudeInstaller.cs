using Rix.Process;

namespace Rix.Claude;

internal static class ClaudeInstaller
{
    internal static async Task<bool> EnsureInstalledAsync(
        CancellationToken cancellationToken,
        TextWriter? error = null,
        Func<string, IEnumerable<string>, CancellationToken, Task<ProcessResult>>? runProcess = null)
    {
        error ??= Console.Error;
        runProcess ??= (fileName, args, cancellationToken) => ProcessWrapper.RunAsync(fileName, args.ToList(),
            workingDirectory: Path.GetTempPath(),
            environmentOverrides: null,
            cancellationToken: cancellationToken);

        if (await RunCommandAsync("claude", ["--version"], runProcess, cancellationToken) is null) return true;

        if (await RunCommandAsync("npm", ["--version"], runProcess, cancellationToken) is { } npmReason)
        {
            await error.WriteLineAsync($"error: claude is not installed and npm could not be run ({npmReason}). Install Node.js to continue.");
            return false;
        }

        if (await RunCommandAsync("npm", ["install", "-g", "@anthropic-ai/claude-code"], runProcess, cancellationToken) is { } installReason)
        {
            await error.WriteLineAsync($"error: npm install -g @anthropic-ai/claude-code failed ({installReason}).");
            return false;
        }

        // Re-verify: npm install can succeed but claude may still not be on PATH.
        if (await RunCommandAsync("claude", ["--version"], runProcess, cancellationToken) is { } verifyReason)
        {
            await error.WriteLineAsync($"error: claude was installed but could not be verified ({verifyReason}).");
            return false;
        }
        return true;
    }

    private static async Task<string?> RunCommandAsync(
        string fileName,
        IEnumerable<string> args,
        Func<string, IEnumerable<string>, CancellationToken, Task<ProcessResult>> runProcess,
        CancellationToken cancellationToken)
    {
        var result = await runProcess(fileName, args, cancellationToken);
        return result is ProcessFailure failure ? failure.Reason : null;
    }
}
