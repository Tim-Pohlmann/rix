namespace Rix;

/// <summary>Writes a file, creating any missing parent directories first — the composition root
/// wires this as a context's file-writing effect. One of the few wrapper types allowed to call
/// <see cref="Directory.CreateDirectory"/>, alongside <see cref="TempDirectory"/> and
/// <see cref="DirectoryMerge"/>.</summary>
internal static class FileWriter
{
    internal static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }
}
