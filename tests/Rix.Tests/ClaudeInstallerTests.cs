using Rix.Claude;
using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class ClaudeInstallerTests
{
    // ---- EnsureInstalledAsync ----

    [TestMethod]
    public async Task EnsureInstalled_ReturnsTrue_WhenClaudeAlreadyInstalled()
    {
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None,
            runProcess: (f, _, _) => Task.FromResult<ProcessResult>(f == "claude"
                ? new ProcessSuccess()
                : new ProcessFailure("exited with code 1")));

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task EnsureInstalled_ReturnsTrue_WhenInstallSucceeds()
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

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task EnsureInstalled_ReturnsFalse_WhenNpmUnavailable()
    {
        var error = new StringWriter();
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None, error: error,
            runProcess: (_, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")));

        Assert.IsFalse(result);
        StringAssert.Contains(error.ToString(), "npm");
    }

    [TestMethod]
    public async Task EnsureInstalled_ReturnsFalse_WhenNpmInstallFails()
    {
        var error = new StringWriter();
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None, error: error,
            runProcess: (f, args, _) => f switch
            {
                "claude" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" when args.Contains("--version") => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                "npm" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                _ => throw new NotSupportedException(f),
            });

        Assert.IsFalse(result);
        StringAssert.Contains(error.ToString(), "npm install");
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
    public async Task EnsureInstalled_ReturnsFalse_WhenTimesOut()
    {
        var error = new StringWriter();
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None, error: error,
            runProcess: (_, _, _) => Task.FromResult<ProcessResult>(new ProcessFailure("timed out")));

        Assert.IsFalse(result);
        StringAssert.Contains(error.ToString(), "timed out");
    }

    [TestMethod]
    public async Task EnsureInstalled_ReturnsFalse_WhenPostInstallClaudeCheckFails()
    {
        var error = new StringWriter();
        var result = await ClaudeInstaller.EnsureInstalledAsync(CancellationToken.None, error: error,
            runProcess: (f, args, _) => f switch
            {
                "claude" => Task.FromResult<ProcessResult>(new ProcessFailure("exited with code 1")),
                "npm" => Task.FromResult<ProcessResult>(new ProcessSuccess()),
                _ => throw new NotSupportedException(f),
            });

        Assert.IsFalse(result);
        StringAssert.Contains(error.ToString(), "could not be verified");
    }
}
