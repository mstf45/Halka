namespace Halka.Core;

public static class OutputPaths
{
    public static string NextTo(string sourcePath, string extension, string suffix = "")
    {
        var full = Path.GetFullPath(sourcePath);
        var directory = Path.GetDirectoryName(full) ?? throw new InvalidOperationException("Klasör yok.");
        var name = Path.GetFileNameWithoutExtension(full);
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var candidate = Path.Combine(directory, name + suffix + ext);

        if (string.Equals(Path.GetFullPath(candidate), full, StringComparison.OrdinalIgnoreCase))
            candidate = Path.Combine(directory, name + suffix + "-kopya" + ext);

        return Unique(candidate);
    }

    public static string Unique(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full) ?? throw new InvalidOperationException("Klasör yok.");
        if (!Exists(full))
        {
            Safety.EnsureInside(directory, full);
            return full;
        }

        var name = Path.GetFileNameWithoutExtension(full);
        var ext = Path.GetExtension(full);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){ext}");
            if (!Exists(candidate))
            {
                Safety.EnsureInside(directory, candidate);
                return candidate;
            }
        }

        throw new IOException("Uygun bir dosya adı bulunamadı.");
    }

    static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
