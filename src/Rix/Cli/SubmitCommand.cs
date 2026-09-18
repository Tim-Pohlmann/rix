using Rix.Submit;
using System.CommandLine;

namespace Rix.Cli;

internal static class SubmitCommand
{
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

    internal static Command Build(Func<SubmitConfig, Task<int>> handler)
    {
        var command = new Command("submit", "Push the branches from a `rix job` result and open their pull requests");

        command.AddOption(CommonOptions.RepoOption);
        command.AddOption(WriteTokenOption);
        command.AddOption(InputDirOption);
        command.AddOption(CommonOptions.WorkDirOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                var config = new SubmitConfig
                (
                    Repo: CommonOptions.ReadRepo(parsed),
                    WriteToken: parsed.Required(WriteTokenOption, "RIX_WRITE_TOKEN", value => new GitToken(value)),
                    InputDir: parsed.Required(InputDirOption, "RIX_INPUT_DIR", path => new DirectoryPath(path)),
                    WorkDir: CommonOptions.ReadWorkDir(parsed)
                );
                ctx.ExitCode = await handler(config);
            }
        );

        return command;
    }
}
