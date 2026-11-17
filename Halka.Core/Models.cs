namespace Halka.Core;

public enum SliceKind
{
    Image,
    ImageSmall,
    Grayscale,
    ImageToPdf,
    MergePdf,
    PdfToImage,
    ZipPack,
    ZipExtract,
    DocxToTxt,
    XlsxToCsv,
    Media
}

public sealed record Slice(string Id, string Label, SliceKind Kind, string? TargetExt);

public sealed record Profile(IReadOnlyList<Slice> Front, IReadOnlyList<Slice> Back, string Note)
{
    public static Profile Empty { get; } = new([], [], "Dosyayı halkanın üzerine bırak");

    public bool HasBack => Back.Count > 0;
}

public sealed record ConvertRequest(
    IReadOnlyList<string> Files,
    Slice Slice,
    int JpegQuality,
    string? FfmpegPath,
    string? OutputDirectory = null);

public sealed record ConvertItem(string Source, bool Ok, string Message, IReadOnlyList<string> Outputs);
