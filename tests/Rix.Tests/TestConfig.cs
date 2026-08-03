using Rix.Job;
using Rix.Submit;

namespace Rix.Tests;

/// <summary>Test helpers for obtaining strongly-typed values that production code only exposes
/// through validating factories. Failing here means the test fixture itself is malformed.</summary>
internal static class TestConfig
{
    internal static JobConfig Valid
    (
        string repo = "owner/repo",
        string prompt = "do it",
        string readToken = "tok",
        string? maxTokens = null,
        string? timeoutMinutes = null,
        string? workDir = null,
        string? outputDir = null,
        string? agent = null,
        string? model = null
    )
    => JobConfig.Create(new JobInputs
    (
        Repo: repo,
        Prompt: prompt,
        ReadToken: readToken,
        MaxTokens: maxTokens,
        TimeoutMinutes: timeoutMinutes,
        WorkDir: workDir ?? Path.GetTempPath(),
        OutputDir: outputDir ?? Path.GetTempPath(),
        Agent: agent,
        Model: model
    )) switch
    {
        JobConfigValid v => v.Config,
        JobConfigInvalid i => throw new AssertFailedException($"invalid test config: {string.Join("; ", i.Errors)}"),
        var other => throw new AssertFailedException($"unexpected result: {other}"),
    };

    internal static SubmitConfig ValidSubmit
    (
        string repo = "owner/repo",
        string writeToken = "tok",
        string? inputDir = null,
        string? workDir = null
    )
    => SubmitConfig.Create(repo, writeToken, inputDir ?? Path.GetTempPath(), workDir ?? Path.GetTempPath()) switch
    {
        SubmitConfigValid v => v.Config,
        SubmitConfigInvalid i => throw new AssertFailedException($"invalid test config: {string.Join("; ", i.Errors)}"),
        var other => throw new AssertFailedException($"unexpected result: {other}"),
    };

    internal static RepoIdentifier Repo(string value) => RepoIdentifier.Parse(value) switch
    {
        ParseSuccess<RepoIdentifier> p => p.Value,
        ParseError<RepoIdentifier> e => throw new AssertFailedException($"invalid repo in test: {e.Error}"),
        var other => throw new AssertFailedException($"unexpected result: {other}"),
    };
}
