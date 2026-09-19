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
        // The same Option instance job registers, so neither command can drift into a different
        // flag name or a different environment variable for the same list.
        command.AddOption(JobOptions.AllowedPushBranchesOption);

        command.SetHandler
        (
            async ctx =>
            {
                var parsed = ctx.ParseResult;
                var result = SubmitConfig.Create
                (
                    repo:       parsed.Str(RepoOption,        "RIX_REPO"),
                    writeToken: parsed.Str(WriteTokenOption,  "RIX_WRITE_TOKEN"),
                    inputDir:   parsed.Str(InputDirOption,    "RIX_INPUT_DIR"),
                    workDir:    parsed.Str(WorkDirOption,     "RIX_WORK_DIR"),
                    allowedPushBranches: parsed.Str(JobOptions.AllowedPushBranchesOption, "RIX_ALLOWED_PUSH_BRANCHES")
                );

                switch (result)
                {
                    case SubmitConfigValid valid:
                        ctx.ExitCode = await handler(valid.Config);
                        break;
                    case SubmitConfigInvalid invalid:
                        foreach (var error in invalid.Errors)
                            Console.Error.WriteLine($"error: {error}");
                        ctx.ExitCode = ExitCodes.SetupFailed;
                        break;
                    default:
                        throw new NotSupportedException($"Unexpected config result: {result.GetType()}");
                }
            }
        );

        return command;
    }
}
