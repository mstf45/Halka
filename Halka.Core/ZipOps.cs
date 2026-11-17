using System.IO.Compression;

namespace Halka.Core;

public static class ZipOps
{
    public static string Pack(IReadOnlyList<string> paths, string output, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var outputFull = Path.GetFullPath(output);
        var entries = 0;
        long bytes = 0;

        try
        {
            using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetFullPath(path), outputFull, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Directory.Exists(path))
                {
                    var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    entries += AddDirectory(zip, path, name, used, ref bytes, cancellationToken, depth: 0);
                }
                else if (File.Exists(path))
                    entries += AddFile(zip, path, Path.GetFileName(path), used, ref bytes);
            }
        }
        catch
        {
            TryDelete(output);
            throw;
        }

        if (entries == 0)
        {
            TryDelete(output);
            throw new InvalidOperationException("Arşive eklenecek dosya yok.");
        }

        return output;
    }

    public static string Extract(string zipPath, string destination, CancellationToken cancellationToken)
    {
        var info = new FileInfo(zipPath);
        if (info.Length > Safety.MaxPackBytes)
            throw new InvalidDataException("Arşiv 8 GB sınırını aşıyor.");

        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var written = new List<string>();

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (zip.Entries.Count > Safety.MaxZipEntries)
                throw new InvalidDataException($"Arşivde {Safety.MaxZipEntries} öğeden fazla var.");

            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(entry.Name) && string.IsNullOrEmpty(entry.FullName))
                    continue;
                if (entry.FullName.Contains(':') || entry.FullName.Contains('\0'))
                    throw new InvalidDataException("Arşiv öğesinin adı geçersiz.");

                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var combined = Path.GetFullPath(Path.Combine(destination, relative));
                Safety.EnsureInside(destination, combined);
                if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(combined, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Arşiv klasör dışına yazmaya çalışıyor.");

                if (relative.EndsWith(Path.DirectorySeparatorChar) || string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(combined);
                    continue;
                }

                Safety.RejectDeviceName(entry.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(combined)!);
                entry.ExtractToFile(combined, overwrite: false);
                written.Add(combined);
            }
        }
        catch
        {
            foreach (var path in written)
                TryDelete(path);
            throw;
        }

        return destination;
    }

    static int AddDirectory(ZipArchive zip, string directory, string prefix, HashSet<string> used, ref long bytes, CancellationToken cancellationToken, int depth)
    {
        if (depth > 40 || IsReparse(directory))
            return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparse(file))
                continue;
            count += AddFile(zip, file, Path.Combine(prefix, Path.GetFileName(file)), used, ref bytes);
        }

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparse(sub))
                continue;
            var name = Path.Combine(prefix, Path.GetFileName(sub));
            count += AddDirectory(zip, sub, name, used, ref bytes, cancellationToken, depth + 1);
        }

        return count;
    }

    static bool IsReparse(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    static int AddFile(ZipArchive zip, string file, string entryName, HashSet<string> used, ref long bytes)
    {
        var length = new FileInfo(file).Length;
        if (bytes + length > Safety.MaxPackBytes)
            throw new InvalidDataException("Arşiv 8 GB sınırını aşıyor.");

        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        Safety.RejectDeviceName(Path.GetFileName(normalized));
        var unique = UniqueEntry(normalized, used);
        zip.CreateEntryFromFile(file, unique, CompressionLevel.Optimal);
        bytes += length;
        return 1;
    }

    static string UniqueEntry(string name, HashSet<string> used)
    {
        if (used.Add(name))
            return name;

        var dir = Path.GetDirectoryName(name)?.Replace('\\', '/') ?? "";
        var baseName = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = string.IsNullOrEmpty(dir) ? $"{baseName} ({i}){ext}" : $"{dir}/{baseName} ({i}){ext}";
            if (used.Add(candidate))
                return candidate;
        }

        throw new IOException("Arşiv içinde ad çakışması çözülemedi.");
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}
