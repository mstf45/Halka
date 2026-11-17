using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Halka.Core;

public static class PdfOps
{
    static readonly object RenderLock = new();

    public static void FromImages(IReadOnlyList<string> images, string output, CancellationToken cancellationToken)
    {
        EnsureFonts();
        using var document = new PdfDocument();
        var temps = new List<string>();
        try
        {
            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var temp = Path.Combine(TempDir(), Guid.NewGuid().ToString("n") + ".png");
                ImageOps.Convert(image, temp, 90, small: false, gray: false, cancellationToken);
                temps.Add(temp);
                using var ximg = XImage.FromFile(temp);
                var page = document.AddPage();
                var w = ximg.PointWidth;
                var h = ximg.PointHeight;
                if (w < 1 || h < 1)
                    throw new InvalidDataException("Görsel boyutu okunamadı.");
                page.Width = XUnit.FromPoint(w);
                page.Height = XUnit.FromPoint(h);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(ximg, 0, 0, w, h);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            document.Save(output);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        finally
        {
            foreach (var temp in temps)
                TryDelete(temp);
        }
    }

    public static void Merge(IReadOnlyList<string> pdfs, string output)
    {
        EnsureFonts();
        using var result = new PdfDocument();
        foreach (var pdf in pdfs)
        {
            var info = new FileInfo(pdf);
            if (info.Length > Safety.MaxDocumentBytes)
                throw new InvalidDataException($"{info.Name} 200 MB sınırını aşıyor.");

            using var input = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
            if (input.PageCount > Safety.MaxPdfPages)
                throw new InvalidDataException($"PDF {Safety.MaxPdfPages} sayfadan uzun.");
            for (var i = 0; i < input.PageCount; i++)
                result.AddPage(input.Pages[i]);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            result.Save(output);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
    }

    public static List<string> Rasterize(string pdf, string targetExtension, string? outputDirectory, CancellationToken cancellationToken)
    {
        var info = new FileInfo(pdf);
        if (info.Length > Safety.MaxDocumentBytes)
            throw new InvalidDataException("PDF 200 MB sınırını aşıyor.");

        var outputs = new List<string>();
        try
        {
            using var stream = File.OpenRead(pdf);
            var pages = PDFtoImage.Conversion.GetPageCount(stream, leaveOpen: true);
            if (pages < 1)
                throw new InvalidDataException("PDF boş.");
            if (pages > Safety.MaxPdfPages)
                throw new InvalidDataException($"PDF {Safety.MaxPdfPages} sayfadan uzun.");

            var directory = string.IsNullOrWhiteSpace(outputDirectory)
                ? Path.GetDirectoryName(info.FullName)!
                : outputDirectory;
            Directory.CreateDirectory(directory);
            var name = Path.GetFileNameWithoutExtension(info.FullName);
            var ext = targetExtension.StartsWith('.') ? targetExtension : "." + targetExtension;
            var options = new PDFtoImage.RenderOptions { Dpi = 144 };

            for (var i = 0; i < pages; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                stream.Position = 0;
                var finalPath = pages == 1 && string.IsNullOrWhiteSpace(outputDirectory)
                    ? OutputPaths.NextTo(pdf, ext)
                    : OutputPaths.Unique(Path.Combine(directory, pages == 1 ? name + ext : $"{name}_s{i + 1:00}{ext}"));

                lock (RenderLock)
                    WritePage(stream, i, ext, finalPath, options, cancellationToken);
                outputs.Add(finalPath);
            }

            return outputs;
        }
        catch
        {
            foreach (var path in outputs)
                TryDelete(path);
            throw;
        }
    }

    static void WritePage(FileStream stream, int page, string ext, string finalPath, PDFtoImage.RenderOptions options, CancellationToken cancellationToken)
    {
        if (ext is ".png" or ".jpg" or ".jpeg" or ".webp")
        {
            if (ext == ".png")
                PDFtoImage.Conversion.SavePng(finalPath, stream, page: page, leaveOpen: true, password: null, options: options);
            else if (ext is ".jpg" or ".jpeg")
                PDFtoImage.Conversion.SaveJpeg(finalPath, stream, page: page, leaveOpen: true, password: null, options: options);
            else
                PDFtoImage.Conversion.SaveWebp(finalPath, stream, page: page, leaveOpen: true, password: null, options: options);
            return;
        }

        var temp = Path.Combine(TempDir(), Guid.NewGuid().ToString("n") + ".png");
        try
        {
            PDFtoImage.Conversion.SavePng(temp, stream, page: page, leaveOpen: true, password: null, options: options);
            ImageOps.Convert(temp, finalPath, 90, small: false, gray: false, cancellationToken);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    static void EnsureFonts()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Halka");
        Directory.CreateDirectory(dir);
        return dir;
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
