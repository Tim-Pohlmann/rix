using Rix.Process;

namespace Rix.Claude;

internal abstract record InstallResult
{
    private protected InstallResult() { }
}
internal sealed record Installed : InstallResult;
internal sealed record InstallFailed(string Reason) : InstallResult;

internal static class ClaudeInstaller
{
    internal static async Task<InstallResult> EnsureInstalledAsync(
        CancellationToken cancellationToken,
        Func<string, IEnumerable<string>, CancellationToken, Task<ProcessResult>>? runProcess = null)
    {
        runProcess ??= (fileName, args, cancellationToken) => ProcessWrapper.RunAsync(fileName, args.ToList(),
            workingDirectory: Path.GetTempPath(),
            environmentOverrides: null,
            cancellationToken: cancellationToken);

        if (await RunCommandAsync("claude", ["--version"], runProcess, cancellationToken) is null) return new Installed();

        if (await RunCommandAsync("npm", ["--version"], runProcess, cancellationToken) is { } npmReason)
            return new InstallFailed($"claude is not installed and npm could not be run ({npmReason}). Install Node.js to continue.");

        if (await RunCommandAsync("npm", ["install", "-g", "@anthropic-ai/claude-code"], runProcess, cancellationToken) is { } installReason)
            return new InstallFailed($"npm install -g @anthropic-ai/claude-code failed ({installReason}).");

        // Re-verify: npm install can succeed but claude may still not be on PATH.
        if (await RunCommandAsync("claude", ["--version"], runProcess, cancellationToken) is { } verifyReason)
            return new InstallFailed($"claude was installed but could not be verified ({verifyReason}).");

        return new Installed();
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
