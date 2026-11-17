namespace Halka.Core;

public static class Profiles
{
    static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".heic", ".heif"
    };

    static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".webm", ".avi", ".wmv", ".m4v", ".mpeg", ".mpg", ".flv"
    };

    static readonly HashSet<string> Audios = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".opus"
    };

    public static Profile For(IReadOnlyList<string> paths, bool ffmpegAvailable)
    {
        if (paths.Count == 0)
            return Profile.Empty;

        if (paths.Any(Directory.Exists))
            return Zip("Klasörler tek arşive toplanır.");

        var files = paths.ToList();

        if (AllIn(files, Images))
        {
            var front = new List<Slice>
            {
                new("png", "PNG", SliceKind.Image, ".png"),
                new("jpg", "JPG", SliceKind.Image, ".jpg"),
                new("webp", "WEBP", SliceKind.Image, ".webp"),
                new("pdf", "PDF", SliceKind.ImageToPdf, ".pdf")
            };
            if (files.Count > 1)
                front.Add(new("merge-pdf", "Tek PDF", SliceKind.MergePdf, ".pdf"));

            var back = new List<Slice>
            {
                new("bmp", "BMP", SliceKind.Image, ".bmp"),
                new("gif", "GIF", SliceKind.Image, ".gif"),
                new("gray", "Gri", SliceKind.Grayscale, null),
                new("small", "Küçük", SliceKind.ImageSmall, ".jpg"),
                new("zip", "ZIP", SliceKind.ZipPack, ".zip")
            };
            return new Profile(front, back, "");
        }

        if (AllOf(files, ".pdf"))
        {
            var front = new List<Slice>
            {
                new("png", "PNG", SliceKind.PdfToImage, ".png"),
                new("jpg", "JPG", SliceKind.PdfToImage, ".jpg"),
                new("webp", "WEBP", SliceKind.PdfToImage, ".webp")
            };
            if (files.Count > 1)
                front.Add(new("merge-pdf", "Birleştir", SliceKind.MergePdf, ".pdf"));

            return new Profile(front, [new("zip", "ZIP", SliceKind.ZipPack, ".zip")], "");
        }

        if (AllOf(files, ".zip"))
            return new Profile([new("extract", "Çıkar", SliceKind.ZipExtract, null)], [], "");

        if (AllOf(files, ".docx"))
            return new Profile([new("txt", "TXT", SliceKind.DocxToTxt, ".txt")], [], "");

        if (AllOf(files, ".xlsx"))
            return new Profile([new("csv", "CSV", SliceKind.XlsxToCsv, ".csv")], [], "");

        if (ffmpegAvailable && AllIn(files, Videos))
        {
            return new Profile(
            [
                new("mp4", "MP4", SliceKind.Media, ".mp4"),
                new("gif", "GIF", SliceKind.Media, ".gif"),
                new("mp3", "MP3", SliceKind.Media, ".mp3")
            ],
            [
                new("webm", "WEBM", SliceKind.Media, ".webm"),
                new("zip", "ZIP", SliceKind.ZipPack, ".zip")
            ], "");
        }

        if (ffmpegAvailable && AllIn(files, Audios))
        {
            return new Profile(
            [
                new("mp3", "MP3", SliceKind.Media, ".mp3"),
                new("wav", "WAV", SliceKind.Media, ".wav"),
                new("flac", "FLAC", SliceKind.Media, ".flac"),
                new("ogg", "OGG", SliceKind.Media, ".ogg")
            ],
            [new("zip", "ZIP", SliceKind.ZipPack, ".zip")], "");
        }

        if (!ffmpegAvailable && files.All(f => Videos.Contains(Ext(f)) || Audios.Contains(Ext(f))))
            return Zip("Ses ve video için kendi FFmpeg.exe dosyanın yolu gerekli.");

        return Zip("");
    }

    static Profile Zip(string note) =>
        new([new Slice("zip", "ZIP", SliceKind.ZipPack, ".zip")], [], note);

    static bool AllIn(IReadOnlyList<string> files, HashSet<string> extensions) =>
        files.Count > 0 && files.All(file => extensions.Contains(Ext(file)));

    static bool AllOf(IReadOnlyList<string> files, params string[] extensions) =>
        AllIn(files, new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase));

    static string Ext(string path) => Path.GetExtension(path);
}
