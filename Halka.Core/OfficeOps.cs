using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Halka.Core;

public static class OfficeOps
{
    public static void DocxToTxt(string input, string output)
    {
        EnsureSize(input);
        var builder = new StringBuilder();
        using (var doc = WordprocessingDocument.Open(input, false))
        {
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidDataException("Belgede gövde yok.");

            foreach (var block in body.Elements())
            {
                if (block is Paragraph paragraph)
                {
                    builder.AppendLine(paragraph.InnerText);
                }
                else if (block is DocumentFormat.OpenXml.Wordprocessing.Table table)
                {
                    foreach (var row in table.Elements<TableRow>())
                    {
                        var cells = row.Elements<TableCell>().Select(cell => cell.InnerText.Trim());
                        builder.AppendLine(string.Join('\t', cells));
                    }
                }
            }
        }

        WriteText(output, builder.ToString());
    }

    public static void XlsxToCsv(string input, string output)
    {
        EnsureSize(input);
        var builder = new StringBuilder();
        using (var doc = SpreadsheetDocument.Open(input, false))
        {
            var workbook = doc.WorkbookPart ?? throw new InvalidDataException("Çalışma kitabı yok.");
            var sheet = workbook.Workbook.Sheets?.Elements<Sheet>().FirstOrDefault()
                ?? throw new InvalidDataException("Sayfa yok.");
            if (workbook.GetPartById(sheet.Id!) is not WorksheetPart worksheet)
                throw new InvalidDataException("Sayfa okunamadı.");

            var shared = workbook.SharedStringTablePart?.SharedStringTable;
            foreach (var row in worksheet.Worksheet.Descendants<Row>())
            {
                var cells = row.Elements<Cell>()
                    .Select(cell => (Index: ColumnIndex(cell.CellReference?.Value), Text: CellText(cell, shared)))
                    .Where(cell => cell.Index >= 0)
                    .OrderBy(cell => cell.Index)
                    .ToList();
                if (cells.Count == 0)
                {
                    builder.AppendLine();
                    continue;
                }

                var width = cells[^1].Index + 1;
                var values = new string[width];
                foreach (var cell in cells)
                    values[cell.Index] = cell.Text;
                builder.AppendLine(string.Join(',', values.Select(Csv)));
            }
        }

        WriteText(output, builder.ToString());
    }

    static void EnsureSize(string input)
    {
        if (new FileInfo(input).Length > Safety.MaxDocumentBytes)
            throw new InvalidDataException("Belge 200 MB sınırını aşıyor.");
    }

    static void WriteText(string output, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        try
        {
            File.WriteAllText(output, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch
        {
            try
            {
                if (File.Exists(output))
                    File.Delete(output);
            }
            catch
            {
            }

            throw;
        }
    }

    static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
            return -1;

        var index = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch))
                break;
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return index - 1;
    }

    static string CellText(Cell cell, SharedStringTable? shared)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InlineString?.InnerText ?? "";

        var raw = cell.CellValue?.InnerText ?? "";
        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex) &&
            shared is not null &&
            sharedIndex >= 0 &&
            sharedIndex < shared.ChildElements.Count)
            return shared.ElementAt(sharedIndex).InnerText;

        if (cell.DataType?.Value == CellValues.Boolean)
            return raw == "1" ? "TRUE" : "FALSE";

        return raw;
    }

    static string Csv(string? value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
