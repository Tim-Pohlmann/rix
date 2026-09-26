using System.CommandLine;

namespace Rix.Cli;

/// <summary>A directory a command was given that has to exist before the command runs, with the
/// name of the input it came from so a missing one is reported under the flag the caller has to
/// fix. The commands never see the file system, so they hand these to their handler rather than
/// checking them; <see cref="Startup"/> does the checking.</summary>
internal sealed record RequiredDirectory(string Name, DirectoryPath Path)
{
    internal static RequiredDirectory For(Option option, DirectoryPath path)
    => new(ParseResultExtensions.Flag(option), path);
}
