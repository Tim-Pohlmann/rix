using Rix.Submit;
using System.CommandLine;

namespace Rix.Cli;

internal static class SubmitCommand
{
    private static readonly Option<string> RepoOption = new
    (
        name: "--repo",
        description: "Full GitHub repo identifier (owner/repo)"
    )
    { IsRequired = false };

    private static readonly Option<string> WriteTokenOption = new
    (
        name: "--write-token",
        description: "GitHub PAT with contents:write and pull-requests:write access"
    )
    { IsRequired = false };

    private static readonly Option<string> InputDirOption = new
    (
        name: "--input-dir",
        description: "Directory holding result.json and the git bundles produced by `rix job`"
    )
    { IsRequired = false };

    private static readonly Option<string> WorkDirOption = new
    (
        name: "--work-dir",
        description: "Base directory for the temp clone (default: system temp)"
    )
    { IsRequired = false };

    internal static Command Build(Func<SubmitConfig, Task<int>> handler)
    {
        var command = new Command("submit", "Push the branches from a `rix job` result and open their pull requests");

        command.AddOption(RepoOption);
        command.AddOption(WriteTokenOption);
        command.AddOption(InputDirOption);
        command.AddOption(WorkDirOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                var config = new SubmitConfig
                (
                    Repo: parsed.Required(RepoOption, "RIX_REPO", value => new RepoIdentifier(value)),
                    WriteToken: parsed.Required(WriteTokenOption, "RIX_WRITE_TOKEN", value => new GitToken(value)),
                    InputDir: parsed.Required(InputDirOption, "RIX_INPUT_DIR", path => new DirectoryPath(path)),
                    WorkDir: parsed.Optional(WorkDirOption, "RIX_WORK_DIR", path => new DirectoryPath(path), () => new DirectoryPath(Path.GetTempPath()))
                );
                ctx.ExitCode = await handler(config);
            }
        );

        return command;
    }
}
