namespace Rix;

/// <summary>Writes a file, creating any missing parent directories first. The one type allowed to
/// call <see cref="Directory.CreateDirectory"/> for a real (non-temp) path — the composition root
/// wires this as a context's file-writing effect. Mirrors <see cref="TempDirectory"/>'s role as
/// the sole home for its filesystem primitive.</summary>
internal static class FileWriter
{
    internal static async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }
}
