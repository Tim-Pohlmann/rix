using System.CommandLine;
using Rix.Submit;

namespace Rix.Cli;

internal static class SubmitCommand
{
    private static readonly Option<string> RepoOption = new(
        name: "--repo",
        description: "Full GitHub repo identifier (owner/repo)")
    { IsRequired = false };

    private static readonly Option<string> WriteTokenOption = new(
        name: "--write-token",
        description: "GitHub PAT with contents:write and pull-requests:write access")
    { IsRequired = false };

    private static readonly Option<string> InputDirOption = new(
        name: "--input-dir",
        description: "Directory holding result.json and the git bundles produced by `rix job`")
    { IsRequired = false };

    private static readonly Option<string> WorkDirOption = new(
        name: "--work-dir",
        description: "Base directory for the temp clone (default: system temp)")
    { IsRequired = false };

    internal static Command Build(Func<SubmitConfig, Task<int>> handler)
    {
        var command = new Command("submit", "Push the branches from a `rix job` result and open their pull requests");

        command.AddOption(RepoOption);
        command.AddOption(WriteTokenOption);
        command.AddOption(InputDirOption);
        command.AddOption(WorkDirOption);

        command.SetHandler(async ctx =>
        {
            string Str(Option<string> opt, string env) =>
                ctx.ParseResult.GetValueForOption(opt) ?? Environment.GetEnvironmentVariable(env) ?? string.Empty;

            var result = SubmitConfig.Create(
                repo:       Str(RepoOption,        "RIX_REPO"),
                writeToken: Str(WriteTokenOption,  "RIX_WRITE_TOKEN"),
                inputDir:   Str(InputDirOption,    "RIX_INPUT_DIR"),
                workDir:    Str(WorkDirOption,     "RIX_WORK_DIR"));

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
        });

        return command;
    }
}
