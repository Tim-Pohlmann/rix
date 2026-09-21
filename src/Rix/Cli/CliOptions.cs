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

    /// <summary>The flag as the user typed it (<c>--repo</c>); <see cref="Option.Name"/> holds the
    /// same text with the dashes stripped, so putting them back is all this takes. Derived rather
    /// than read off <see cref="Option.Aliases"/>, which is a set whose enumeration order is
    /// undefined: an option with a short alias too would then have its errors reported under
    /// whichever of the two the set happened to yield, and reported differently from one run to the
    /// next. Deriving leaves options free to declare as many aliases as they want, and
    /// <c>CliOptionsTests</c> checks the result really is one the option accepts.</summary>
    internal static string Flag(Option option) => $"--{option.Name}";
}
