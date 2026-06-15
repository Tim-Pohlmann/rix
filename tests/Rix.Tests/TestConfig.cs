using Rix.Job;

namespace Rix.Tests;

/// <summary>Test helpers for obtaining strongly-typed values that production code only exposes
/// through validating factories. Failing here means the test fixture itself is malformed.</summary>
internal static class TestConfig
{
    internal static JobConfig Valid(
        string repo = "owner/repo",
        string prompt = "do it",
        string readToken = "tok",
        int? maxTokens = null,
        int? timeoutMinutes = null,
        string? workDir = null,
        string? outputDir = null) =>
        JobConfig.Create(repo, prompt, readToken, maxTokens, timeoutMinutes,
            workDir ?? Path.GetTempPath(), outputDir ?? Path.GetTempPath()) switch
        {
            JobConfigValid v => v.Config,
            JobConfigInvalid i => throw new AssertFailedException($"invalid test config: {string.Join("; ", i.Errors)}"),
            var other => throw new AssertFailedException($"unexpected result: {other}"),
        };

    internal static RepoIdentifier Repo(string value) => RepoIdentifier.Parse(value) switch
    {
        ParseSuccess<RepoIdentifier> p => p.Value,
        ParseError<RepoIdentifier> e => throw new AssertFailedException($"invalid repo in test: {e.Error}"),
        var other => throw new AssertFailedException($"unexpected result: {other}"),
    };
}
