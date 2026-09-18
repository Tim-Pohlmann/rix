using Rix.CiFailure;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Cli;

/// <summary>CLI options shared by <c>ci-failure</c> and <c>ci-failure-job</c>, which both identify
/// the same workflow run to inspect.</summary>
internal static class CiFailureOptions
{
    internal static readonly Option<string> RepoOption = new
    (
        name: "--repo",
        description: "Full GitHub repo identifier (owner/repo) that produced the run"
    )
    { IsRequired = false };

    internal static readonly Option<string> RunIdOption = new
    (
        name: "--run-id",
        description: "ID of the (possibly failed) workflow run to inspect"
    )
    { IsRequired = false };

    internal static readonly Option<string> ReadTokenOption = new
    (
        name: "--read-token",
        description: "GitHub PAT with read access to the repo, including Actions:read"
    )
    { IsRequired = false };

    /// <summary>Turns the shared options into a <see cref="CiFailureConfig"/>; the first malformed
    /// value throws <see cref="InvalidInputException"/>, which <see cref="CliPipeline"/> reports.</summary>
    internal static CiFailureConfig ReadConfig(ParseResult parsed)
    => new
    (
        Repo: Input.Required("--repo", parsed.Str(RepoOption, "RIX_REPO"), value => new RepoIdentifier(value)),
        ReadToken: Input.Required("--read-token", parsed.Str(ReadTokenOption, "RIX_READ_TOKEN"), value => new GitReadToken(value)),
        RunId: Input.Required("--run-id", parsed.Str(RunIdOption, "RIX_RUN_ID"), Input.Positive<long>)
    );
}
