namespace Rix;

/// <summary>Writes a single diagnostic line (e.g. a forwarded agent stdout line) to the log sink.</summary>
internal delegate void LogLine(string line);

/// <summary>A read-scoped GitHub access token: enough to clone and inspect a repo, never to write.
/// <see cref="GitToken"/> derives from it because a write-capable token can do everything a read
/// one can, so a <see cref="GitToken"/> is accepted wherever a <c>GitReadToken</c> is required.</summary>
internal record GitReadToken(string Value);

/// <summary>A write-capable GitHub access token (push, open PRs). Being a <see cref="GitReadToken"/>,
/// it also satisfies read-only consumers without a separate credential.</summary>
internal sealed record GitToken(string Value) : GitReadToken(Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);

/// <summary>How many rix-authored commits may sit at the tip of a branch before
/// <c>rix ci-failure</c> stops reacting to that branch's failures — the bound on rix answering its
/// own output forever. At least one, since zero would refuse every branch rix has ever touched,
/// and at most <see cref="MaxValue"/>: the streak is read in a single request, so a cap beyond one
/// page could not be told apart from no cap at all.</summary>
internal readonly record struct MaxRixCommits
{
    /// <summary>GitHub's largest <c>per_page</c> for the commits endpoint.</summary>
    internal const int MaxValue = 100;

    internal int Value { get; }

    internal MaxRixCommits(int value)
    {
        if (value < 1 || value > MaxValue)
            throw new InvalidInputException($"must be between 1 and {MaxValue}, got '{value}'");
        Value = value;
    }
}

/// <summary>The GitHub Actions identifier of a single workflow run. A distinct type rather than a
/// bare <c>long</c> so it can't be transposed with the other numbers threaded through the same
/// calls (a PR number, a job count).</summary>
internal readonly record struct RunId(long Value);

/// <summary>A validated GitHub <c>owner/name</c> identifier. The constructor is the single source
/// of the format rule: it throws <see cref="InvalidInputException"/> for anything else, so any
/// <c>RepoIdentifier</c> that exists is guaranteed well-formed.</summary>
internal sealed record RepoIdentifier
{
    internal string Value { get; }

    /// <summary>The <c>owner</c> segment, needed to scope a pull-request lookup to same-repo
    /// branches: GitHub's <c>/pulls?head=</c> filter matches <c>owner:branch</c>, not branch name
    /// alone. Split off once here, where the separator has already been located, rather than
    /// re-scanning <see cref="Value"/> on every read.</summary>
    internal string Owner { get; }

    internal RepoIdentifier(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
            throw new InvalidInputException($"'{value}' is not a valid repo identifier; expected owner/name format.");
        Value = value;
        Owner = value[..slash];
    }

    public override string ToString() => Value;
}

/// <summary>A directory path that existed when the instance was constructed, stored as an
/// absolute path — the constructor throws <see cref="InvalidInputException"/> otherwise.
/// Normalising to absolute at the boundary means paths derived from it (e.g. via
/// <see cref="System.IO.Path.Combine(string, string)"/>) stay rooted, so a subprocess run from a
/// different working directory resolves them where the caller intended.</summary>
internal sealed record DirectoryPath
{
    internal string Value { get; }

    internal DirectoryPath(string path)
    {
        if (!Directory.Exists(path))
            throw new InvalidInputException($"directory does not exist: {path}");
        Value = Path.GetFullPath(path);
    }

    public override string ToString() => Value;
}
