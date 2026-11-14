using Rix.CodingAgents;
using Rix.Cli;
using Rix.Agent;
using System.CommandLine;

namespace Rix.Tests;

/// <summary><see cref="AgentOptions.AddTo"/> registers this set in one call, but <c>agent</c>'s
/// <c>CreateConfig</c> reads it back one option at a time, so a flag can be registered - and
/// documented, and accepted on the command line without complaint - while nothing ever reads it.
/// Nothing but these tests connects the two halves: a flag dropped from <c>CreateConfig</c> is
/// silently ignored at runtime, taking the default instead of what the caller asked for.
///
/// So every flag is listed exactly once below, with the value it's invoked with and the assertion
/// that it reached its own slot in the resulting config, and <c>agent</c> is then driven with that
/// whole set. <see cref="AddTo_RegistersExactlyTheFlagsCoveredHere"/> keeps the list honest:
/// registering an eleventh option without adding a row here fails rather than going uncovered.
///
/// <c>ci-failure</c> runs no agent and takes only a few of these flags, so those are covered by
/// <see cref="CiFailureCommandTests"/> alongside the ones it rejects.</summary>
[TestClass]
public class SharedAgentOptionsTests
{
    // Two distinct directories that really exist, rather than Path.GetTempPath() for both:
    // DirectoryPath rejects a path that isn't there, and a CreateConfig reading --work-dir into
    // OutputDir (or vice versa) is only visible if the two values differ.
    private string _workDir = string.Empty;
    private string _outputDir = string.Empty;

    [TestInitialize]
    public void CreateDirectories()
    {
        _workDir = Directory.CreateTempSubdirectory("rix-shared-work-").FullName;
        _outputDir = Directory.CreateTempSubdirectory("rix-shared-out-").FullName;
    }

    /// <summary>Every flag <see cref="AgentOptions.AddTo"/> registers, each with a value that differs
    /// both from that option's default (so a CreateConfig that never reads it can't accidentally pass)
    /// and from every other value here (so a CreateConfig reading one option into another's slot can't
    /// either).</summary>
    private (string Flag, string Value, Action<AgentConfig> AssertArrived)[] SharedFlags =>
    [
        ("--repo", "shared/repo", config => Assert.AreEqual("shared/repo", config.Repo.ToString())),
        ("--read-token", "shared-read-token", config => Assert.AreEqual("shared-read-token", config.ReadToken.Value)),
        ("--max-tokens", "12345", config => Assert.AreEqual(12345, config.Agent.MaxTokens.Value)),
        ("--timeout", "17", config => Assert.AreEqual(17, config.TimeoutMinutes.Value)),
        ("--work-dir", _workDir, config => Assert.AreEqual(_workDir, config.WorkDir.Value)),
        ("--output-dir", _outputDir, config => Assert.AreEqual(_outputDir, config.OutputDir.Value)),
        ("--agent", "claude", config => Assert.AreEqual(AgentKind.Claude, config.Agent.Kind)),
        ("--model", "vendor/shared-model", config => Assert.AreEqual("vendor/shared-model", config.Agent.Model)),
        ("--effort", "shared-effort", config => Assert.AreEqual("shared-effort", config.Agent.Effort)),
        ("--agent-api-key", "shared-api-key", config => Assert.AreEqual("shared-api-key", config.Agent.Credential?.Key)),
        ("--agent-api-key-env", "SHARED_API_KEY", config => Assert.AreEqual("SHARED_API_KEY", config.Agent.Credential?.EnvName)),
    ];

    private string[] SharedArgs
    => [.. SharedFlags.SelectMany(flag => new[] { flag.Flag, flag.Value })];

    private void AssertEveryFlagArrived(AgentConfig config)
    {
        foreach (var flag in SharedFlags)
        {
            flag.AssertArrived(config);
        }
    }

    [TestMethod]
    public void AddTo_RegistersExactlyTheFlagsCoveredHere()
    {
        var command = new Command("probe");
        AgentOptions.AddTo(command);

        CollectionAssert.AreEquivalent(
            SharedFlags.Select(flag => flag.Flag).ToArray(),
            command.Options.Select(option => option.Name).ToArray());
    }

    [TestMethod]
    public void AgentCommand_ReadsEverySharedFlag()
    {
        var parsed = CommandArgs.Parse(AgentCommand.Build(), ["--prompt", "shared prompt", .. SharedArgs]);

        AssertEveryFlagArrived(AgentCommand.CreateConfig(parsed, new LocalFileSystem()));
    }
}
