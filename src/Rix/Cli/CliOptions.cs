using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Cli;

/// <summary>Shared CLI option resolution: prefer the parsed option, fall back to an environment
/// variable, then to a default. Used by every command handler.</summary>
internal static class CliOptions
{
    public static string Str(this ParseResult parseResult, Option<string> option, string env) =>
        parseResult.GetValueForOption(option) ?? Environment.GetEnvironmentVariable(env) ?? string.Empty;

    public static int? Int(this ParseResult parseResult, Option<int?> option, string env) =>
        parseResult.GetValueForOption(option)
        ?? (int.TryParse(Environment.GetEnvironmentVariable(env), out var n) ? n : null);
}
