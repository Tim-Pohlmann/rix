namespace Rix.FileSystem;

/// <summary>Writes a file, creating any missing parent directories first — the composition root
/// wires this as a context's file-writing effect and writes <c>rix job</c>'s output files through
/// it. Like everything in <see cref="Rix.FileSystem"/>, it is one of the only places allowed to
/// write to disk directly.</summary>
internal static class FileWriter
{
    internal static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }
}
