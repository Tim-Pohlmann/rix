using System.CommandLine;

namespace Rix.Cli;

/// <summary>The <c>ci-failure</c> options that identify which workflow run to inspect, kept
/// alongside <see cref="JobOptions"/> (which the same command also takes) rather than mixed into
/// it, since these say nothing about how the agent is run.</summary>
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
