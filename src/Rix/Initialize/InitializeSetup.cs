using System.Text.RegularExpressions;

namespace Rix.Initialize;

/// <summary>Which kind of repo <c>rix initialize</c> sets up. It picks the templates written and
/// the steps printed afterwards. Pattern-matched; never cast.</summary>
internal interface IInitializeSetup;

/// <summary>A repo that runs rix on itself and holds its own tokens.</summary>
internal sealed record StandaloneSetup : IInitializeSetup;

/// <summary>A factory repo: it runs rix against the repos it serves, holds the tokens for all of
/// them, and answers the CI failures they dispatch to it.</summary>
internal sealed record FactorySetup : IInitializeSetup;

/// <summary>A repo a factory serves: it runs no rix of its own and holds none of rix's tokens. It
/// only dispatches its CI failures to <see cref="FactoryRepo"/>.</summary>
internal sealed partial record FactoryServedSetup : IInitializeSetup
{
    internal RepoIdentifier FactoryRepo { get; }

    /// <summary>Narrower than <see cref="RepoIdentifier"/>, because the name is pasted into a YAML
    /// file the caller then commits: a quote or a space in it would surface as a broken workflow in
    /// their Actions tab rather than here. These are the characters GitHub allows in owner and repo
    /// names.</summary>
    internal FactoryServedSetup(RepoIdentifier factoryRepo)
    {
        if (!GitHubRepoName().IsMatch(factoryRepo.Value))
            throw new InvalidInputException($"'{factoryRepo.Value}' is not a GitHub repo name; expected owner/name with letters, digits, '-', '_' or '.'.");
        FactoryRepo = factoryRepo;
    }

    // Anchored with \z, not $: $ also matches before a trailing newline, which would carry into
    // the written file.
    [GeneratedRegex(@"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+\z")]
    private static partial Regex GitHubRepoName();
}
