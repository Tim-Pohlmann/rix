using Rix.Claude;
using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class ClaudeInstallerTests
{
    // ---- EnsureInstalledAsync ----

    [TestMethod]
    public async Task EnsureInstalled_ReturnsInstalled_WhenClaudeAlreadyInstalled()
    {
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None,
            runProcess: (f, _, _) => Task.FromResult<ProcessResult>(f == "claude"
                ? new ProcessSuccess()
                : new ProcessFailure("exited with code 1")));

        Assert.IsInstanceOfType<Installed>(result);
    }

    [TestMethod]
    public async Task EnsureInstalled_ReturnsInstalled_WhenInstallSucceeds()
    {
        int claudeCallCount = 0;
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None,
            runProcess: (f, args, _) => f switch
            {
                "claude" => Task.FromResult<ProcessResult>(++claudeCallCount == 1
                    ? new ProcessFailure("exited with code 1")
                    : new ProcessSuccess()),
                "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                _ => throw new NotSupportedException(f),
            });

        Assert.IsInstanceOfType<Installed>(result);
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenNpmUnavailable()
    {
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None,
            runProcess: (_, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")));

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "npm");
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenNpmInstallFails()
    {
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None,
            runProcess: (f, args, _) => f switch
            {
                "claude" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" when args.Contains("--version") => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                "npm" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                _ => throw new NotSupportedException(f),
            });

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "npm install");
    }

    [TestMethod]
    public async Task EnsureInstalled_InstallsLatestVersion()
    {
        string? installedPackage = null;
        int claudeCallCount = 0;

        await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None,
            runProcess: (f, args, _) =>
            {
                if (f == "npm" && args.Contains("install"))
                    installedPackage = args.FirstOrDefault(a => a.StartsWith("@anthropic-ai/claude-code"));
                return f switch
                {
                    "claude" => Task.FromResult<ProcessResult>(++claudeCallCount == 1
                        ? new ProcessFailure("exited with code 1")
                        : new ProcessSuccess()),
                    "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                    _ => throw new NotSupportedException(f),
                };
            });

        Assert.AreEqual("@anthropic-ai/claude-code", installedPackage);
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenTimesOut()
    {
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None,
            runProcess: (_, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("timed out")));

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "timed out");
    }

    [TestMethod]
    public async Task EnsureInstalled_Fails_WhenPostInstallClaudeCheckFails()
    {
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None,
            runProcess: (f, args, _) => f switch
            {
                "claude" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                _ => throw new NotSupportedException(f),
            });

        Assert.IsInstanceOfType<InstallFailed>(result, out var failed);
        StringAssert.Contains(failed.Reason, "could not be verified");
    }
}
