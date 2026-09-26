using Rix.Agents;
using Rix.CiFailure;
using Rix.Cli;
using Rix.Job;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Tests;

/// <summary><see cref="JobOptions.AddTo"/> registers the shared flags once, but each command reads
/// them back one call at a time in its own handler, so a flag can be registered - and documented,
/// and accepted on the command line without complaint - while one of the two commands never reads
/// it. Nothing but these tests connects the two halves: a flag dropped from <c>ci-failure</c>'s
/// handler is silently ignored at runtime, taking the default instead of what the caller asked for.
///
/// So every flag is listed exactly once below, with the value it's invoked with and the assertion
/// that it reached its own slot in the resulting config, and both commands are then driven with
/// that whole set. <see cref="AddTo_RegistersExactlyTheFlagsCoveredHere"/> keeps the list honest:
/// registering an eleventh option without adding a row here fails rather than going uncovered.</summary>
[TestClass]
public class SharedJobOptionsTests
{
    // Two distinct directories that really exist, rather than Path.GetTempPath() for both:
    // DirectoryPath rejects a path that isn't there, and a handler reading --work-dir into
    // OutputDir (or vice versa) is only visible if the two values differ.
    private string _workDir = string.Empty;
    private string _outputDir = string.Empty;

    [TestInitialize]
    public void CreateDirectories()
    {
        _workDir = Directory.CreateTempSubdirectory("rix-shared-work-").FullName;
        _outputDir = Directory.CreateTempSubdirectory("rix-shared-out-").FullName;
    }

    /// <summary>Every flag <see cref="JobOptions.AddTo"/> registers, each with a value that differs
    /// both from that option's default (so a handler that never reads it can't accidentally pass)
    /// and from every other value here (so a handler reading one option into another's slot can't
    /// either).</summary>
    private (string Flag, string Value, Action<JobConfig> AssertArrived)[] SharedFlags =>
    [
        ("--repo", "shared/repo", job => Assert.AreEqual("shared/repo", job.Repo.ToString())),
        ("--read-token", "shared-read-token", job => Assert.AreEqual("shared-read-token", job.ReadToken.Value)),
        ("--max-tokens", "12345", job => Assert.AreEqual(12345, job.Agent.MaxTokens.Value)),
        ("--timeout", "17", job => Assert.AreEqual(17, job.TimeoutMinutes.Value)),
        ("--work-dir", _workDir, job => Assert.AreEqual(_workDir, job.WorkDir.Value)),
        ("--output-dir", _outputDir, job => Assert.AreEqual(_outputDir, job.OutputDir.Value)),
        ("--agent", "claude", job => Assert.AreEqual(AgentKind.Claude, job.Agent.Kind)),
        ("--model", "vendor/shared-model", job => Assert.AreEqual("vendor/shared-model", job.Agent.Model)),
        ("--agent-api-key", "shared-api-key", job => Assert.AreEqual("shared-api-key", job.Agent.Credential?.Key)),
        ("--agent-api-key-env", "SHARED_API_KEY", job => Assert.AreEqual("SHARED_API_KEY", job.Agent.Credential?.EnvName)),
    ];

    private string[] SharedArgs
    => [.. SharedFlags.SelectMany(flag => new[] { flag.Flag, flag.Value })];

    private void AssertEveryFlagArrived(JobConfig job)
    {
        foreach (var flag in SharedFlags)
        {
            flag.AssertArrived(job);
        }
    }

    [TestMethod]
    public void AddTo_RegistersExactlyTheFlagsCoveredHere()
    {
        var command = new Command("probe");
        JobOptions.AddTo(command);

        CollectionAssert.AreEquivalent(
            SharedFlags.Select(flag => flag.Flag).ToArray(),
            command.Options.Select(option => option.Aliases.Single()).ToArray());
    }

    [TestMethod]
    public async Task JobCommand_ReadsEverySharedFlag()
    {
        JobConfig? captured = null;
        var root = new RootCommand();
        root.AddCommand(JobCommand.Build(new LocalFileSystem(), config =>
        {
            captured = config;
            return Task.FromResult(0);
        }));

        string[] args = ["job", "--prompt", "shared prompt", .. SharedArgs];
        await CliPipeline.Build(root).InvokeAsync(args);

        Assert.IsNotNull(captured);
        AssertEveryFlagArrived(captured);
    }

    [TestMethod]
    public async Task CiFailureCommand_ReadsEverySharedFlag()
    {
        // Read back through ToJobConfig for the same reason CiFailureCommandTests does: the shared
        // flags end up in the job half of the config, and the two values only a detected failure
        // can supply are stubbed out here.
        CiFailureConfig? captured = null;
        var root = new RootCommand();
        root.AddCommand(CiFailureCommand.Build(new LocalFileSystem(), config =>
        {
            captured = config;
            return Task.FromResult(0);
        }));

        string[] args = ["ci-failure", "--run-id", "1", .. SharedArgs];
        await CliPipeline.Build(root).InvokeAsync(args);

        Assert.IsNotNull(captured);
        AssertEveryFlagArrived(captured.ToJobConfig("fix it", new BranchName("rix/fix")));
    }
}
