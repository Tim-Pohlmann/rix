using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;

namespace Rix;

internal static class Startup
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var rootCommand = new RootCommand("RIX - AI-powered code automation");
        var commandLineBuilder = new CommandLineBuilder(rootCommand)
            .UseDefaults();

        var parser = commandLineBuilder.Build();
        return await parser.InvokeAsync(args);
    }
}
