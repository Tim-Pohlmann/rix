using Rix.Claude;
using Rix.Job;
using Rix.Process;

namespace Rix.Tests;

[TestClass]
public class StartupTests
{
    [TestMethod]
    public async Task RunAsync_WithHelpFlag_ReturnsZero()
    {
        var exitCode = await Startup.RunAsync(["--help"]);
        Assert.AreEqual(0, exitCode);
    }

    [TestMethod]
    public async Task RunJobAsync_Returns2_WhenConfigIsInvalid()
    {
        var exitCode = await Startup.RunAsync(["job"]);
        Assert.AreEqual(2, exitCode);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task RunJobAsync_PrintsErrorsAndReturns2_WhenValidationFails()
    {
        var stderr = new StringWriter();
        var original = Console.Error;
        Console.SetError(stderr);
        try
        {
            // Valid repo (so FromInputs does not throw) but empty prompt -> a validation error.
            var config = JobConfig.FromInputs("owner/repo", prompt: "", readToken: "tok",
                maxTokens: null, timeoutMinutes: null, workDir: null, outputDir: Path.GetTempPath());

            var exitCode = await Startup.RunJobAsync(config);

            Assert.AreEqual(ExitCodes.SetupFailed, exitCode);
            StringAssert.Contains(stderr.ToString(), "--prompt is required");
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [TestMethod]
    public async Task RunJobAsync_RunsJobAndReturnsZero_WhenConfigIsValid()
    {
        var outputDir = Directory.CreateTempSubdirectory("rix-out-").FullName;
        try
        {
            var config = JobConfig.FromInputs("owner/repo", prompt: "Do something", readToken: "tok",
                maxTokens: null, timeoutMinutes: null, workDir: null, outputDir: outputDir);

            RunProcessAsync runner = (f, a, d, e, onLine, ct) =>
                Task.FromResult<ProcessResult>(new ProcessSuccess());

            var exitCode = await Startup.RunJobAsync(config,
                host: new StubRepositoryHost(),
                processRunner: runner,
                claudeInstaller: _ => Task.FromResult<InstallResult>(new Installed()));

            Assert.AreEqual(ExitCodes.Success, exitCode);
            Assert.IsTrue(File.Exists(Path.Combine(outputDir, "result.json")));
        }
        finally
        {
            try { Directory.Delete(outputDir, recursive: true); } catch (DirectoryNotFoundException) { }
        }
    }
}
