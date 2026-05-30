namespace Rix;

internal readonly record struct ReadToken(string Value);
internal readonly record struct WriteToken(string Value);
internal readonly record struct MaxTokens(int Value);
internal readonly record struct TimeoutMinutes(int Value);

internal readonly record struct RepoIdentifier(string Value)
{
    internal string Owner => Value.Split('/', 2)[0];
    internal string Name => Value.Split('/', 2)[1];
}
