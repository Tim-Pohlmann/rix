using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Rix.Cli;
using Rix.Job;

namespace Rix;

internal static class Startup
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var rootCommand = new RootCommand("RIX - AI-powered code automation");

        rootCommand.AddCommand(JobCommand.Build(RunJobAsync));

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }

    private static Task<int> RunJobAsync(JobConfig config)
    {
        var errors = config.Validate();
        if (errors.Count > 0)
        {
            foreach (var error in errors)
                Console.Error.WriteLine($"error: {error}");
            return Task.FromResult(2);
        }

        // Job execution will be wired in a later PR.
        throw new NotImplementedException("Job execution not yet implemented");
    }
}
