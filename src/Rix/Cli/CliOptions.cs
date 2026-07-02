using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Cli;

/// <summary>Shared CLI option resolution: prefer the parsed option, fall back to an environment
/// variable, then to a default. Used by every command handler.</summary>
internal static class ParseResultExtensions
{
    extension(ParseResult parseResult)
    {
        public string Str(Option<string> option, string env) =>
            parseResult.GetValueForOption(option) ?? Environment.GetEnvironmentVariable(env) ?? string.Empty;

        public int? Int(Option<int?> option, string env) =>
            parseResult.GetValueForOption(option)
            ?? (int.TryParse(Environment.GetEnvironmentVariable(env), out var n) ? n : null);
    }
}
