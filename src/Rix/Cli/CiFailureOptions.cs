using System.CommandLine;

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
}
