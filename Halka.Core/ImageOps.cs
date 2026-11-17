using System.Runtime.InteropServices;
using ImageMagick;
using SkiaSharp;

namespace Halka.Core;

public static class ImageOps
{
    public static void Convert(string input, string output, int quality, bool small, bool gray, CancellationToken cancellationToken)
    {
        if (!File.Exists(input))
            throw new FileNotFoundException("Görsel bulunamadı.", input);

        cancellationToken.ThrowIfCancellationRequested();
        using var decoded = OpenBitmap(input);
        SKBitmap? resized = null;
        var source = decoded;
        try
        {
            if (small && (source.Width > 1600 || source.Height > 1600))
            {
                var scale = 1600.0 / Math.Max(source.Width, source.Height);
                var width = Math.Max(1, (int)Math.Round(source.Width * scale));
                var height = Math.Max(1, (int)Math.Round(source.Height * scale));
                resized = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
                    ?? throw new InvalidDataException("Görsel küçültülemedi.");
                source = resized;
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new InvalidDataException("Görsel yüzeyi açılamadı.");
            using var paint = new SKPaint();
            if (gray)
            {
                paint.ColorFilter = SKColorFilter.CreateColorMatrix(
                [
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0.2126f, 0.7152f, 0.0722f, 0, 0,
                    0, 0, 0, 1, 0
                ]);
            }

            surface.Canvas.DrawBitmap(source, 0, 0, paint);
            using var snapshot = surface.Snapshot();
            using var data = snapshot.Encode(FormatOf(output), quality) ?? throw new InvalidDataException("Görsel yazılamadı.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var stream = File.Create(output);
            data.SaveTo(stream);
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        finally
        {
            resized?.Dispose();
        }
    }

    static SKBitmap OpenBitmap(string input)
    {
        var ext = Path.GetExtension(input);
        if (ext.Equals(".heic", StringComparison.OrdinalIgnoreCase) || ext.Equals(".heif", StringComparison.OrdinalIgnoreCase))
            return OpenHeic(input);

        using var codec = SKCodec.Create(input) ?? throw new InvalidDataException("Bu görsel okunamadı.");
        if ((long)codec.Info.Width * codec.Info.Height > Safety.MaxPixels)
            throw new InvalidDataException("Görsel çok büyük.");
        return SKBitmap.Decode(codec) ?? throw new InvalidDataException("Bu görsel okunamadı.");
    }

    static SKBitmap OpenHeic(string input)
    {
        try
        {
            using var image = new MagickImage(input);
            image.AutoOrient();
            image.ColorType = ColorType.TrueColorAlpha;
            if ((long)image.Width * image.Height > Safety.MaxPixels)
                throw new InvalidDataException("Görsel çok büyük.");

            var width = (int)image.Width;
            var height = (int)image.Height;
            using var pixels = image.GetPixels() ?? throw new InvalidDataException("Bu HEIC okunamadı.");
            var bytes = pixels.ToByteArray(PixelMapping.RGBA) ?? throw new InvalidDataException("Bu HEIC okunamadı.");
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            if (bytes.Length < info.RowBytes * (long)height)
                throw new InvalidDataException("Bu HEIC okunamadı.");

            var bitmap = new SKBitmap(info);
            Marshal.Copy(bytes, 0, bitmap.GetPixels(), info.RowBytes * height);
            return bitmap;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (MagickException)
        {
            throw new InvalidDataException("Bu HEIC okunamadı.");
        }
    }

    public static string GrayExtension(string source)
    {
        var ext = Path.GetExtension(source).ToLowerInvariant();
        return ext is ".png" or ".webp" or ".jpg" or ".jpeg" or ".bmp" or ".gif"
            ? ext == ".jpeg" ? ".jpg" : ext
            : ".jpg";
    }

    static SKEncodedImageFormat FormatOf(string output) => Path.GetExtension(output).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
        ".png" => SKEncodedImageFormat.Png,
        ".webp" => SKEncodedImageFormat.Webp,
        ".bmp" => SKEncodedImageFormat.Bmp,
        ".gif" => SKEncodedImageFormat.Gif,
        _ => throw new NotSupportedException("Bu görsel biçimi yazılamıyor.")
    };

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
