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

/// <summary>A validated GitHub <c>owner/name</c> identifier. The constructor is the single source
/// of the format rule: it throws <see cref="InvalidInputException"/> for anything else, so any
/// <c>RepoIdentifier</c> that exists is guaranteed well-formed.</summary>
internal sealed record RepoIdentifier
{
    internal string Value { get; }

    internal RepoIdentifier(string value)
    {
        var slash = value.IndexOf('/');
        if (slash <= 0 || slash == value.Length - 1 || value.IndexOf('/', slash + 1) >= 0)
            throw new InvalidInputException($"'{value}' is not a valid repo identifier; expected owner/name format.");
        Value = value;
    }

    /// <summary>The <c>owner</c> segment, needed to scope a pull-request lookup to same-repo
    /// branches: GitHub's <c>/pulls?head=</c> filter matches <c>owner:branch</c>, not branch name
    /// alone.</summary>
    internal string Owner => Value[..Value.IndexOf('/')];

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
