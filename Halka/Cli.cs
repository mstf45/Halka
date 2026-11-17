using System.IO;
using System.Text;
using Halka.Core;

namespace Halka;

static class Cli
{
    public static bool TryRun(string[] args, out int code)
    {
        code = 0;
        if (args.Length == 0)
            return false;

        AttachParentConsole();
        var command = args[0].ToLowerInvariant();
        if (command is "-h" or "--help" or "help")
        {
            Console.WriteLine(Help);
            return true;
        }

        if (command != "convert")
        {
            Console.Error.WriteLine("Bilinmeyen komut. `Halka help` yaz.");
            code = 2;
            return true;
        }

        var files = new List<string>();
        string? to = null;
        var merge = false;
        var quality = 85;
        string? ffmpeg = null;
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--to" && i + 1 < args.Length)
                to = args[++i];
            else if (arg == "--quality" && i + 1 < args.Length && int.TryParse(args[++i], out var parsed))
                quality = parsed;
            else if (arg == "--ffmpeg" && i + 1 < args.Length)
                ffmpeg = args[++i];
            else if (arg == "--merge")
                merge = true;
            else if (!arg.StartsWith('-'))
                files.Add(Path.GetFullPath(arg));
        }

        if (files.Count == 0 || string.IsNullOrWhiteSpace(to))
        {
            Console.Error.WriteLine(Help);
            code = 2;
            return true;
        }

        ffmpeg = FfmpegOps.Find(ffmpeg);
        var profile = Profiles.For(files, ffmpeg != null);
        var slices = profile.Front.Concat(profile.Back).ToList();
        Slice? slice = null;
        if (merge)
            slice = slices.FirstOrDefault(item => item.Kind == SliceKind.MergePdf);
        slice ??= slices.FirstOrDefault(item =>
            item.Id.Equals(to, StringComparison.OrdinalIgnoreCase) ||
            item.Label.Equals(to, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.TargetExt?.TrimStart('.'), to.TrimStart('.'), StringComparison.OrdinalIgnoreCase));

        if (slice is null)
        {
            Console.Error.WriteLine(string.IsNullOrWhiteSpace(profile.Note)
                ? "Bu dosyalar için o biçim yok."
                : profile.Note);
            code = 2;
            return true;
        }

        var results = Converter.Run(new ConvertRequest(files, slice, quality, ffmpeg), null, CancellationToken.None);
        foreach (var result in results)
        {
            var name = string.IsNullOrEmpty(result.Source) ? "dosya" : Path.GetFileName(result.Source);
            if (result.Ok)
                Console.WriteLine($"{name} → {result.Message}");
            else
                Console.Error.WriteLine($"{name}: {result.Message}");
        }

        code = results.All(result => result.Ok) ? 0 : 1;
        return true;
    }

    static void AttachParentConsole()
    {
        if (!NativeMethods.AttachConsole(-1))
            NativeMethods.AllocConsole();
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch (IOException)
        {
        }
    }

    const string Help = """
        Halka convert dosya.png --to webp
        Halka convert a.png b.png --to pdf --merge
        Halka convert belge.docx --to txt
        Halka convert tablo.xlsx --to csv
        Halka convert arsiv.zip --to extract
        Halka convert video.mp4 --to mp3 --ffmpeg C:\tools\ffmpeg.exe

        Biçimler: png, jpg, webp, bmp, gif, tiff, pdf, gray, small, zip, txt, csv, extract
        FFmpeg kuruluysa: mp4, gif, mp3, wav, flac, ogg, webm
        """;
}
