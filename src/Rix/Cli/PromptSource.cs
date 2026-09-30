namespace Rix.Cli;

/// <summary>Where <c>job</c>'s prompt comes from: the text itself, or a file holding it. The
/// command never sees the file system, so a file is only named here; <see cref="Startup"/> reads
/// it.</summary>
internal abstract record PromptSource;

internal sealed record PromptText(string Text) : PromptSource;

internal sealed record PromptFile(string Path) : PromptSource;
