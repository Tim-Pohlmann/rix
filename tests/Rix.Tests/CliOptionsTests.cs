using Rix.Cli;
using System.CommandLine;
using System.Reflection;

namespace Rix.Tests;

[TestClass]
public class CliOptionsTests
{
    /// <summary>Error messages name the offending flag via <see cref="ParseResultExtensions.Flag"/>,
    /// which rebuilds it from <see cref="Option.Name"/> instead of picking one of the option's
    /// aliases. That derivation is only right while every option is declared under a name the user
    /// can actually type, so this checks the rebuilt flag against the aliases the option accepts —
    /// declaring one as <c>-r</c> alone, or adding extra aliases, is answered here at build time
    /// rather than by a wrong flag name in an error the user sees.</summary>
    [TestMethod]
    public void EveryOption_ReportsErrorsUnderAnAliasItAccepts()
    {
        var options = DeclaredOptions().ToList();

        // Guards the reflection itself: a rename that empties this list would pass every assertion
        // below without checking anything.
        Assert.IsTrue(options.Count > 10, $"expected rix to declare more than 10 options, found {options.Count}");
        foreach (var (owner, option) in options)
            CollectionAssert.Contains(option.Aliases.ToArray(), ParseResultExtensions.Flag(option), owner);
    }

    /// <summary>Every option rix declares, found by reflection rather than listed here, so an option
    /// added to a command is covered without anyone remembering to add it.</summary>
    private static IEnumerable<(string Owner, Option Option)> DeclaredOptions()
    => typeof(ParseResultExtensions).Assembly.GetTypes()
        .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        .Where(field => typeof(Option).IsAssignableFrom(field.FieldType))
        .Select(field => ($"{field.DeclaringType!.Name}.{field.Name}", (Option)field.GetValue(null)!));
}
