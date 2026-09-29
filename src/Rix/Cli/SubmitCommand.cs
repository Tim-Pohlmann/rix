using Rix.Submit;
using System.CommandLine;

namespace Rix.Cli;

internal static class SubmitCommand
{
    private static readonly Option<string> WriteTokenOption = new("--write-token")
    {
        Description = "GitHub PAT with contents:write and pull-requests:write access"
    };

    private static readonly Option<string> InputDirOption = new("--input-dir")
    {
        Description = "Directory holding result.json and the git bundles produced by `rix job`"
    };

    internal static Command Build()
    {
        var command = new Command("submit", "Push the branches from a `rix job` result and open their pull requests");

        command.Options.Add(CommonOptions.RepoOption);
        command.Options.Add(WriteTokenOption);
        command.Options.Add(InputDirOption);
        command.Options.Add(CommonOptions.WorkDirOption);
        // The same Option instance job registers, so neither command can drift into a different
        // flag name or a different environment variable for the same list.
        command.Options.Add(JobOptions.AllowedPushBranchesOption);

        return command;
    }

    internal static SubmitConfig ReadConfig(ParseResult parsed, string systemTempDirectory)
    => new
    (
        CommonOptions.ReadRepo(parsed),
        parsed.Required(WriteTokenOption, "RIX_WRITE_TOKEN", value => new GitToken(value)),
        InputDir: parsed.Required(InputDirOption, "RIX_INPUT_DIR", path => new DirectoryPath(path)),
        WorkDir: CommonOptions.ReadWorkDir(parsed, systemTempDirectory),
        // Unparseable input is impossible: every branch name is acceptable, and a blank value
        // means the empty list, which is the safe end of the range rather than an error.
        BranchName.ParseAllowList(parsed.Str(JobOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES"))
    );

    internal static IReadOnlyList<RequiredDirectory> RequiredDirectories(SubmitConfig config)
    => [RequiredDirectory.For(InputDirOption, config.InputDir), RequiredDirectory.For(CommonOptions.WorkDirOption, config.WorkDir)];
}
