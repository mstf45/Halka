using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using Halka.Core;
using SkiaSharp;

namespace Halka.Tests;

public class HalkaTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "HalkaTests", Guid.NewGuid().ToString("n"));

    public HalkaTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Format_list_matches_resolution_rows()
    {
        var json = """
        {
          "title": "Ornek",
          "duration": 548,
          "formats": [
            {"format_id":"312","ext":"mp4","vcodec":"avc1.64002A","acodec":"none","height":1080,"fps":50,"tbr":4167,"protocol":"m3u8_native"},
            {"format_id":"299","ext":"mp4","vcodec":"avc1.64002A","acodec":"none","height":1080,"fps":50,"tbr":2014,"filesize":440075834,"protocol":"https"},
            {"format_id":"136","ext":"mp4","vcodec":"avc1.64001F","acodec":"none","height":720,"fps":25,"tbr":769,"filesize":168129185,"protocol":"https"},
            {"format_id":"298","ext":"mp4","vcodec":"avc1.640020","acodec":"none","height":720,"fps":50,"tbr":1063,"filesize":232329299,"protocol":"https"},
            {"format_id":"18","ext":"mp4","vcodec":"avc1.42001E","acodec":"mp4a.40.2","height":360,"fps":25,"tbr":400,"filesize":77985251,"protocol":"https"},
            {"format_id":"134","ext":"mp4","vcodec":"avc1.4D401E","acodec":"none","height":360,"fps":25,"tbr":600,"filesize":49942143,"protocol":"https"},
            {"format_id":"140-0","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":129,"filesize":100,"protocol":"https","format_note":"English (US), medium"},
            {"format_id":"140-3","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":129.5,"filesize":28288672,"protocol":"https","format_note":"Turkish original (default), medium"},
            {"format_id":"140-drc","ext":"m4a","vcodec":"none","acodec":"mp4a.40.2","abr":129,"filesize":28288660,"protocol":"https","format_note":"Turkish original, medium, DRC"},
            {"format_id":"251-2","ext":"webm","vcodec":"none","acodec":"opus","abr":127,"filesize":27811942,"protocol":"https","format_note":"Turkish original, medium"}
          ]
        }
        """;

        var catalog = FormatList.Parse(json);
        var video = catalog.Offers.Where(item => item.Kind == "video").ToList();

        Assert.Equal("Ornek", catalog.Title);
        Assert.Equal("00:09:08", catalog.DurationLabel);
        Assert.Equal(new[] { "1080p50", "720p50", "360p" }, video.Select(item => item.Label).ToArray());
        Assert.Equal("299+140-3", video[0].Selector);
        Assert.Equal("446.67 MB", video[0].SizeLabel);
        Assert.Equal("mp4", video[0].MergeExt);
        Assert.True(video[0].NeedsFfmpeg);
        Assert.Equal("74.61 MB", video[2].SizeLabel);
        Assert.Contains(catalog.Offers, item => item.Kind == "audio" && item.Label == "M4A" && item.Selector == "140-3" && !item.NeedsFfmpeg);
        Assert.Contains(catalog.Offers, item => item.Kind == "audio" && item.Label == "MP3" && item.AudioFormat == "mp3" && item.NeedsFfmpeg);
        Assert.Contains(catalog.Offers, item => item.Kind == "audio" && item.Label == "WAV");
        Assert.Contains(catalog.Offers, item => item.Kind == "audio" && item.Label == "OPUS" && item.Selector == "251-2");
    }

    [Fact]
    public void Urls_accept_public_https_only()
    {
        Assert.True(UrlRules.TryHttpUrl("https://www.youtube.com/watch?v=abc", out var youtube));
        Assert.Equal("www.youtube.com", youtube.Host);
        Assert.True(UrlRules.TryHttpUrl("youtu.be/abc", out _));
        Assert.False(UrlRules.TryHttpUrl("file:///C:/gizli.pdf", out _));
        Assert.False(UrlRules.TryHttpUrl("javascript:alert(1)", out _));
        Assert.DoesNotContain("/", UrlRules.SafeStem(youtube));
        Assert.DoesNotContain("..", UrlRules.SafeStem(youtube));
    }

    [Fact]
    public void Hit_selects_slice_by_clock_position()
    {
        const double size = 400;
        Assert.Equal(0, DonutMath.Hit(200, 60, size, size, 4));
        Assert.Equal(1, DonutMath.Hit(340, 200, size, size, 4));
        Assert.Equal(2, DonutMath.Hit(200, 340, size, size, 4));
        Assert.Equal(3, DonutMath.Hit(60, 200, size, size, 4));
        Assert.Equal(-1, DonutMath.Hit(200, 200, size, size, 4));
        Assert.Equal(-1, DonutMath.Hit(200, 0, size, size, 4));
        Assert.Equal(-1, DonutMath.Hit(10, 10, size, size, 0));
    }

    [Fact]
    public void Gesture_opens_after_armed_drag()
    {
        var tracker = new GestureTracker { Armed = true };
        tracker.Press(0, 0);
        Assert.False(tracker.Moved(10, 0));
        Assert.True(tracker.Moved(30, 0));
        tracker.Open = true;
        Assert.False(tracker.Moved(80, 0));
        tracker.Release();
        var idle = new GestureTracker();
        idle.Press(0, 0);
        Assert.False(idle.Moved(40, 40));
    }

    [Fact]
    public void Profiles_follow_the_files()
    {
        var images = Profiles.For([PathOf("a.png"), PathOf("b.jpg"), PathOf("c.heic")], ffmpegAvailable: false);
        Assert.Contains(images.Front, slice => slice.Id == "merge-pdf");
        Assert.Contains(images.Back, slice => slice.Id == "gray");

        var one = Profiles.For([PathOf("a.png")], ffmpegAvailable: false);
        Assert.DoesNotContain(one.Front, slice => slice.Kind == SliceKind.MergePdf);

        var pdfs = Profiles.For([PathOf("a.pdf"), PathOf("b.pdf")], ffmpegAvailable: false);
        Assert.Contains(pdfs.Front, slice => slice.Label == "Birleştir");

        Assert.Equal("txt", Profiles.For([PathOf("a.docx")], false).Front[0].Id);
        Assert.Equal("csv", Profiles.For([PathOf("a.xlsx")], false).Front[0].Id);
        Assert.Equal("extract", Profiles.For([PathOf("a.zip")], false).Front[0].Id);
        Assert.Contains("FFmpeg", Profiles.For([PathOf("a.mp4")], false).Note);
        Assert.Equal("zip", Profiles.For([PathOf("a.exe"), PathOf("b.txt")], false).Front[0].Id);
    }

    [Fact]
    public void Output_names_stay_beside_the_source_and_do_not_overwrite()
    {
        var source = Path.Combine(_dir, "foto.jpg");
        File.WriteAllBytes(source, [1]);
        var next = OutputPaths.NextTo(source, ".jpg");
        Assert.Equal(_dir, Path.GetDirectoryName(next));
        Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(next));
        Assert.EndsWith(".jpg", next);
    }

    [Fact]
    public void Image_converts_to_jpeg_pdf_and_gray()
    {
        var png = Path.Combine(_dir, "renk.png");
        WritePng(png, 40, 24, new SKColor(0xE0, 0xA0, 0x45));

        var chosen = Path.Combine(_dir, "secilen");
        Directory.CreateDirectory(chosen);
        var jpeg = Converter.Run(new ConvertRequest([png], new Slice("jpg", "JPG", SliceKind.Image, ".jpg"), 80, null, chosen), null, CancellationToken.None);
        Assert.True(jpeg[0].Ok, jpeg[0].Message);
        Assert.StartsWith(chosen, jpeg[0].Outputs[0], StringComparison.OrdinalIgnoreCase);

        var heic = Path.Combine(_dir, "telefon.heic");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic"), heic);
        var fromHeic = Converter.Run(Req(heic, "jpg"), null, CancellationToken.None);
        Assert.True(fromHeic[0].Ok, fromHeic[0].Message);
        using (var savedHeic = SKBitmap.Decode(fromHeic[0].Outputs[0]))
            Assert.True(savedHeic.Width > 0 && savedHeic.Height > 0);

        var other = Path.Combine(_dir, "diger.png");
        WritePng(other, 10, 10, new SKColor(0x11, 0x22, 0x33));
        var both = Converter.Run(new ConvertRequest([png, other], new Slice("jpg", "JPG", SliceKind.Image, ".jpg"), 80, null, chosen), null, CancellationToken.None);
        Assert.Equal(2, both.Count);
        Assert.All(both, item =>
        {
            Assert.True(item.Ok, item.Message);
            Assert.StartsWith(chosen, item.Outputs[0], StringComparison.OrdinalIgnoreCase);
        });

        jpeg = Converter.Run(Req(png, "jpg"), null, CancellationToken.None);
        Assert.True(jpeg[0].Ok, jpeg[0].Message);
        using (var saved = SKBitmap.Decode(jpeg[0].Outputs[0]))
            Assert.Equal(40, saved.Width);

        var pdf = Converter.Run(Req(png, "pdf"), null, CancellationToken.None);
        Assert.True(pdf[0].Ok, pdf[0].Message);
        var header = File.ReadAllBytes(pdf[0].Outputs[0])[..4];
        Assert.Equal("%PDF"u8.ToArray(), header);

        var gray = Converter.Run(Req(png, "gray"), null, CancellationToken.None);
        Assert.True(gray[0].Ok, gray[0].Message);
        Assert.EndsWith("-gri.png", gray[0].Outputs[0]);

        var wide = Path.Combine(_dir, "genis.png");
        WritePng(wide, 2000, 20, new SKColor(0x33, 0x66, 0x99));
        var small = Converter.Run(Req(wide, "small"), null, CancellationToken.None);
        Assert.True(small[0].Ok, small[0].Message);
        using var resized = SKBitmap.Decode(small[0].Outputs[0]);
        Assert.True(resized.Width <= 1600);
    }

    [Fact]
    public void Pdf_page_becomes_png_and_two_images_merge()
    {
        var first = Path.Combine(_dir, "bir.png");
        var second = Path.Combine(_dir, "iki.png");
        WritePng(first, 30, 30, new SKColor(0x11, 0x22, 0x33));
        WritePng(second, 30, 30, new SKColor(0xAA, 0xBB, 0xCC));

        var merged = Converter.Run(new ConvertRequest([first, second], new Slice("merge-pdf", "Tek PDF", SliceKind.MergePdf, ".pdf"), 80, null), null, CancellationToken.None);
        Assert.True(merged[0].Ok, merged[0].Message);
        var pages = Converter.Run(Req(merged[0].Outputs[0], "png"), null, CancellationToken.None);
        Assert.True(pages[0].Ok, pages[0].Message);
        Assert.Equal(2, pages[0].Outputs.Count);
    }

    [Fact]
    public void Docx_and_xlsx_become_text()
    {
        var docx = Path.Combine(_dir, "yazi.docx");
        using (var doc = WordprocessingDocument.Create(docx, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Merhaba Halka")))));
            main.Document.Save();
        }

        var txt = Converter.Run(Req(docx, "txt"), null, CancellationToken.None);
        Assert.True(txt[0].Ok, txt[0].Message);
        Assert.Contains("Merhaba Halka", File.ReadAllText(txt[0].Outputs[0]));

        var xlsx = Path.Combine(_dir, "tablo.xlsx");
        using (var sheet = SpreadsheetDocument.Create(xlsx, SpreadsheetDocumentType.Workbook))
        {
            var workbook = sheet.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var part = workbook.AddNewPart<WorksheetPart>();
            var row = new Row();
            row.Append(new Cell
            {
                CellReference = "A1",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new DocumentFormat.OpenXml.Spreadsheet.Text("ad"))
            });
            row.Append(new Cell
            {
                CellReference = "B1",
                DataType = CellValues.Number,
                CellValue = new CellValue("2")
            });
            part.Worksheet = new Worksheet(new SheetData(row));
            workbook.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = workbook.GetIdOfPart(part),
                SheetId = 1,
                Name = "Sayfa1"
            });
            workbook.Workbook.Save();
        }

        var csv = Converter.Run(Req(xlsx, "csv"), null, CancellationToken.None);
        Assert.True(csv[0].Ok, csv[0].Message);
        Assert.Contains("ad,2", File.ReadAllText(csv[0].Outputs[0]));
    }

    [Fact]
    public void Zip_roundtrip_rejects_escape()
    {
        var source = Path.Combine(_dir, "not.txt");
        File.WriteAllText(source, "icerik", Encoding.UTF8);
        var packed = Converter.Run(Req(source, "zip"), null, CancellationToken.None);
        Assert.True(packed[0].Ok, packed[0].Message);

        var extracted = Converter.Run(Req(packed[0].Outputs[0], "extract"), null, CancellationToken.None);
        Assert.True(extracted[0].Ok, extracted[0].Message);
        Assert.True(File.Exists(Path.Combine(extracted[0].Outputs[0], "not.txt")));

        var evil = Path.Combine(_dir, "kotu.zip");
        using (var zip = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../kacak.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hayir");
        }

        var parent = Directory.GetParent(_dir)!.FullName;
        var leaked = Path.Combine(parent, "kacak.txt");
        var result = Converter.Run(Req(evil, "extract"), null, CancellationToken.None);
        Assert.False(result[0].Ok);
        Assert.False(File.Exists(leaked));
    }

    static void WritePng(string path, int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    string PathOf(string name) => Path.Combine(_dir, name);

    ConvertRequest Req(string file, string id)
    {
        var profile = Profiles.For([file], ffmpegAvailable: false);
        var slice = profile.Front.Concat(profile.Back).First(item =>
            item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.TargetExt?.TrimStart('.'), id, StringComparison.OrdinalIgnoreCase));
        return new ConvertRequest([file], slice, 80, null);
    }
}
