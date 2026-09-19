using System.CommandLine;
using System.CommandLine.Parsing;

namespace Rix.Cli;

/// <summary>Shared CLI option resolution: prefer the parsed option, fall back to an environment
/// variable, then to a default. Used by every command handler. The typed readers hand the raw
/// text to <see cref="Input"/> under the option's own flag name, so an error always names the
/// flag exactly as it was declared rather than a retyped copy of it.</summary>
internal static class ParseResultExtensions
{
    extension(ParseResult parseResult)
    {
        public string Str(Option<string> option, string env)
        => parseResult.GetValueForOption(option) ?? Environment.GetEnvironmentVariable(env) ?? string.Empty;

        /// <inheritdoc cref="Input.Required{T}"/>
        public T Required<T>(Option<string> option, string env, Func<string, T> construct)
        => Input.Required(Flag(option), parseResult.Str(option, env), construct);

        /// <inheritdoc cref="Input.RequiredText"/>
        public string RequiredText(Option<string> option, string env)
        => Input.RequiredText(Flag(option), parseResult.Str(option, env));

        /// <inheritdoc cref="Input.Optional{T}(string, string?, Func{string, T}, T)"/>
        public T Optional<T>(Option<string> option, string env, Func<string, T> construct, T fallback)
        => Input.Optional(Flag(option), parseResult.Str(option, env), construct, fallback);

        /// <inheritdoc cref="Input.Optional{T}(string, string?, Func{string, T}, Func{T})"/>
        public T Optional<T>(Option<string> option, string env, Func<string, T> construct, Func<T> fallback)
        => Input.Optional(Flag(option), parseResult.Str(option, env), construct, fallback);

        /// <inheritdoc cref="Input.Named{T}"/>
        public T Named<T>(Option<string> option, string env, Func<string, T> construct)
        => Input.Named(Flag(option), () => construct(parseResult.Str(option, env)));

        /// <inheritdoc cref="Input.OptionalText"/>
        public string? OptionalText(Option<string> option, string env)
        => Input.OptionalText(parseResult.Str(option, env));
    }

    /// <summary>The flag as the user typed it (<c>--repo</c>); <see cref="Option.Name"/> would
    /// drop the dashes. Every rix option is declared with exactly one alias.</summary>
    private static string Flag(Option option) => option.Aliases.Single();
}
