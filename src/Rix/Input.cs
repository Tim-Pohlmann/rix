using System.Numerics;

namespace Rix;

/// <summary>The one place raw caller input meets a value object's constructor. Each helper checks
/// the blank-vs-supplied question the constructor can't (an empty <c>--repo</c> is "missing", not
/// "malformed"), then prefixes any <see cref="InvalidInputException"/> the constructor throws with
/// the input's <c>name</c> — so the message names the flag or field the caller has to fix, while
/// the value object itself only knows about its own format rule.</summary>
internal static class Input
{
    /// <summary>A value the caller must supply: blank is an error naming <paramref name="name"/>,
    /// otherwise <paramref name="construct"/> turns the raw text into the typed value.</summary>
    internal static T Required<T>(string name, string? raw, Func<string, T> construct)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidInputException($"{name} is required");
        return Named(name, () => construct(raw));
    }

    /// <summary>A value the caller may omit: blank yields <paramref name="fallback"/>, anything
    /// else must construct successfully — a typo never silently falls back to the default.</summary>
    internal static T Optional<T>(string name, string? raw, Func<string, T> construct, T fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        return Named(name, () => construct(raw));
    }

    /// <summary>Plain text the caller may omit, normalised so blank and absent are the same
    /// <c>null</c> — e.g. <c>--model</c>, where unset means "let the agent CLI pick".</summary>
    internal static string? OptionalText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return raw;
    }

    /// <summary>Runs <paramref name="construct"/> and re-raises any <see cref="InvalidInputException"/>
    /// with <paramref name="name"/> prefixed, for values built from more than one raw input (e.g.
    /// the agent credential's env var name, which depends on the agent kind too).</summary>
    internal static T Named<T>(string name, Func<T> construct)
    {
        try
        {
            return construct();
        }
        catch (InvalidInputException ex)
        {
            throw new InvalidInputException($"{name}: {ex.Message}", ex);
        }
    }

    /// <summary>Parses a numeric flag such as <c>--max-tokens</c> or <c>--run-id</c>, which only
    /// ever make sense as a positive whole number.</summary>
    internal static T Positive<T>(string raw) where T : INumber<T>
    {
        if (!T.TryParse(raw, null, out var parsed) || parsed <= T.Zero)
            throw new InvalidInputException($"must be a positive integer, got '{raw}'");
        return parsed;
    }
}
