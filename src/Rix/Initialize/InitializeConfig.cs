namespace Rix.Initialize;

internal sealed record InitializeConfig
{
    internal DirectoryPath TargetDir { get; }

    /// <summary>Private so an <see cref="InitializeConfig"/> can only be produced by
    /// <see cref="Create"/>, which guarantees every field is validated — the type can never exist
    /// in an invalid state.</summary>
    private InitializeConfig(DirectoryPath targetDir) => TargetDir = targetDir;

    /// <summary>Validates the raw <c>--dir</c> input into a strongly-typed
    /// <see cref="InitializeConfig"/>. Only an <see cref="InitializeConfigValid"/> carries a
    /// directory that existed when it was checked.</summary>
    internal static InitializeConfigResult Create(string dir)
    {
        var errors = new List<string>();

        DirectoryPath? parsedDir = null;
        if (string.IsNullOrWhiteSpace(dir))
            errors.Add("--dir is required");
        else
            parsedDir = DirectoryPath.Parse(dir).Collect(errors, "--dir");

        if (errors.Count > 0)
            return new InitializeConfigInvalid([.. errors]);

        // Non-null here: a blank or unresolvable path would have added an error above.
        return new InitializeConfigValid(new InitializeConfig(parsedDir!));
    }
}

/// <summary>The result of <see cref="InitializeConfig.Create"/>: a validated config or the list of
/// reasons it was rejected. Pattern-matched by callers; never cast.</summary>
internal abstract record InitializeConfigResult
{
    private protected InitializeConfigResult() { }
}

internal sealed record InitializeConfigValid(InitializeConfig Config) : InitializeConfigResult;

internal sealed record InitializeConfigInvalid(IReadOnlyList<string> Errors) : InitializeConfigResult;
