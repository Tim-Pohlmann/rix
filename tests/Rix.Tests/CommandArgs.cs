using System.CommandLine;

namespace Rix.Tests;

/// <summary>Parses <c>args</c> the way <c>rix</c> would once <paramref name="command"/> is one of
/// its verbs, so a test hands the result straight to that command's <c>ReadConfig</c> — no
/// invocation, no handler.</summary>
internal static class CommandArgs
{
    internal static ParseResult Parse(Command command, params string[] args)
    {
        var root = new RootCommand { command };
        return root.Parse(args);
    }
}
