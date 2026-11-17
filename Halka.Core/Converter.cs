namespace Halka.Core;

public static class Converter
{
    public static IReadOnlyList<ConvertItem> Run(ConvertRequest request, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var files = request.Files
            .Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
            return [Fail("", "Dosya bulunamadı.")];

        var quality = Math.Clamp(request.JpegQuality, 1, 100);
        try
        {
            return request.Slice.Kind switch
            {
                SliceKind.ZipPack => [Pack(files, request.OutputDirectory, cancellationToken)],
                SliceKind.MergePdf => [Merge(files, request.OutputDirectory, cancellationToken)],
                _ => Each(files, request.Slice, quality, request.FfmpegPath, request.OutputDirectory, progress, cancellationToken)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return [Fail(files[0], ex.Message)];
        }
    }

    static IReadOnlyList<ConvertItem> Each(
        List<string> files,
        Slice slice,
        int quality,
        string? ffmpeg,
        string? outputDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var list = new List<ConvertItem>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"{i + 1} / {files.Count}   {Path.GetFileName(files[i])}");
            list.Add(One(files[i], slice, quality, ffmpeg, outputDirectory, cancellationToken));
        }

        return list;
    }

    static ConvertItem One(string file, Slice slice, int quality, string? ffmpeg, string? outputDirectory, CancellationToken cancellationToken)
    {
        var made = new List<string>();
        try
        {
            switch (slice.Kind)
            {
                case SliceKind.Image:
                    made.Add(WriteImage(file, slice.TargetExt!, quality, small: false, gray: false, outputDirectory, cancellationToken));
                    break;
                case SliceKind.ImageSmall:
                    made.Add(WriteImage(file, ".jpg", quality, small: true, gray: false, outputDirectory, cancellationToken));
                    break;
                case SliceKind.Grayscale:
                    made.Add(WriteImage(file, ImageOps.GrayExtension(file), quality, small: false, gray: true, "-gri", outputDirectory, cancellationToken));
                    break;
                case SliceKind.ImageToPdf:
                {
                    var pdf = Place(file, ".pdf", "", outputDirectory);
                    PdfOps.FromImages([file], pdf, cancellationToken);
                    made.Add(pdf);
                    break;
                }
                case SliceKind.PdfToImage:
                    made.AddRange(PdfOps.Rasterize(file, slice.TargetExt ?? ".png", outputDirectory, cancellationToken));
                    break;
                case SliceKind.ZipExtract:
                {
                    var parent = string.IsNullOrWhiteSpace(outputDirectory)
                        ? Path.GetDirectoryName(Path.GetFullPath(file))!
                        : outputDirectory;
                    Directory.CreateDirectory(parent);
                    var folder = OutputPaths.Unique(Path.Combine(parent, Path.GetFileNameWithoutExtension(file)));
                    made.Add(ZipOps.Extract(file, folder, cancellationToken));
                    break;
                }
                case SliceKind.DocxToTxt:
                {
                    var txt = Place(file, ".txt", "", outputDirectory);
                    OfficeOps.DocxToTxt(file, txt);
                    made.Add(txt);
                    break;
                }
                case SliceKind.XlsxToCsv:
                {
                    var csv = Place(file, ".csv", "", outputDirectory);
                    OfficeOps.XlsxToCsv(file, csv);
                    made.Add(csv);
                    break;
                }
                case SliceKind.Media:
                {
                    var tool = FfmpegOps.Find(ffmpeg) ?? throw new InvalidOperationException("FFmpeg bulunamadı.");
                    var media = Place(file, slice.TargetExt ?? ".mp4", "", outputDirectory);
                    FfmpegOps.Convert(tool, file, media, cancellationToken);
                    made.Add(media);
                    break;
                }
                default:
                    throw new NotSupportedException("Bu dilim tek dosyada çalışmıyor.");
            }

            return Ok(file, made);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            foreach (var path in made)
                TryDelete(path);
            return Fail(file, ex.Message);
        }
    }

    static string WriteImage(string file, string extension, int quality, bool small, bool gray, string? outputDirectory, CancellationToken cancellationToken) =>
        WriteImage(file, extension, quality, small, gray, gray ? "-gri" : small ? "-kucuk" : "", outputDirectory, cancellationToken);

    static string WriteImage(string file, string extension, int quality, bool small, bool gray, string suffix, string? outputDirectory, CancellationToken cancellationToken)
    {
        var output = Place(file, extension, suffix, outputDirectory);
        ImageOps.Convert(file, output, quality, small, gray, cancellationToken);
        return output;
    }

    static string Place(string file, string extension, string suffix, string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return OutputPaths.NextTo(file, extension, suffix);
        Directory.CreateDirectory(outputDirectory);
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        var name = Path.GetFileNameWithoutExtension(file);
        return OutputPaths.Unique(Path.Combine(outputDirectory, name + suffix + ext));
    }

    static ConvertItem Pack(List<string> files, string? outputDirectory, CancellationToken cancellationToken)
    {
        var output = Place(files[0], ".zip", "-arsiv", outputDirectory);
        try
        {
            ZipOps.Pack(files, output, cancellationToken);
            return Ok(files[0], [output]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(output);
            return Fail(files[0], ex.Message);
        }
    }

    static ConvertItem Merge(List<string> files, string? outputDirectory, CancellationToken cancellationToken)
    {
        var output = Place(files[0], ".pdf", "-birlesik", outputDirectory);
        try
        {
            if (files.All(file => string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase)))
                PdfOps.Merge(files, output);
            else
                PdfOps.FromImages(files, output, cancellationToken);
            return Ok(files[0], [output]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TryDelete(output);
            return Fail(files[0], ex.Message);
        }
    }

    static ConvertItem Ok(string source, IReadOnlyList<string> made)
    {
        var message = made.Count switch
        {
            0 => "Tamam",
            1 => Path.GetFileName(made[0]),
            _ => $"{made.Count} dosya · {Path.GetFileName(made[0])}"
        };
        return new ConvertItem(source, true, message, made);
    }

    static ConvertItem Fail(string source, string message) =>
        new(source, false, string.IsNullOrWhiteSpace(message) ? "Dönüşüm tamamlanamadı." : message, []);

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
