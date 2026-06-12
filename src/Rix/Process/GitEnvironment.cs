namespace Rix.Process;

/// <summary>The minimal environment git needs to run: an inherited <c>PATH</c> (to locate the
/// git executable) and <c>HOME</c> (for global config / credentials). Single source for both
/// the repository host's clone and the bundle command.</summary>
internal static class GitEnvironment
{
    internal static IReadOnlyDictionary<string, string> Current { get; } = new Dictionary<string, string>
    {
        ["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "",
        ["HOME"] = Environment.GetEnvironmentVariable("HOME") ?? "",
    };
}
