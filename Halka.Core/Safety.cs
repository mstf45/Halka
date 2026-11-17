namespace Halka.Core;

public static class Safety
{
    public const long MaxDocumentBytes = 200L * 1024 * 1024;
    public const long MaxPackBytes = 8L * 1024 * 1024 * 1024;
    public const int MaxZipEntries = 5000;
    public const int MaxPdfPages = 300;
    public const long MaxPixels = 80_000_000;

    public static void EnsureInside(string rootDirectory, string candidatePath)
    {
        var root = Path.GetFullPath(rootDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        var candidate = Path.GetFullPath(candidatePath);
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Çıkış yolu kaynak klasörünün dışına çıkıyor.");
    }

    public static void RejectDeviceName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(baseName))
            throw new InvalidDataException("Arşivde boş dosya adı var.");

        string[] reserved =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ];

        if (reserved.Contains(baseName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Arşivde Windows'un ayırdığı bir dosya adı var.");
    }
}
