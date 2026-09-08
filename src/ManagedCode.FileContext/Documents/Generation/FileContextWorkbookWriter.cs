using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ManagedCode.FileContext;

internal static class FileContextWorkbookWriter
{
    private const int MaximumSheetNameLength = 31;
    private const int MaximumRows = 1_048_576;
    private const int MaximumColumns = 16_384;
    private const int MaximumCellTextLength = 32_767;

    public static byte[] Create(FileContextWorkbook workbook, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (workbook.Sheets is null) { throw new ArgumentException("Workbook sheets are required.", nameof(workbook)); }
        if (workbook.Sheets.Count == 0) { throw new ArgumentException("A workbook needs at least one sheet.", nameof(workbook)); }
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var part = document.AddWorkbookPart();
            part.Workbook = new Workbook { Sheets = new Sheets() };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sheet in workbook.Sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSheet(sheet, names);
                var worksheet = part.AddNewPart<WorksheetPart>();
                worksheet.Worksheet = new Worksheet();
                worksheet.Worksheet.AppendChild(CreateRows(sheet.Rows, cancellationToken));
                part.Workbook.GetFirstChild<Sheets>()!.AppendChild(new Sheet
                {
                    Id = part.GetIdOfPart(worksheet),
                    SheetId = (uint)names.Count,
                    Name = sheet.Name
                });
            }
            part.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static void ValidateSheet(FileContextWorksheet sheet, HashSet<string> names)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        if (string.IsNullOrWhiteSpace(sheet.Name)) { throw new ArgumentException("Sheet name is required.", nameof(sheet)); }
        if (sheet.Name.Length > MaximumSheetNameLength || sheet.Name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0 ||
            sheet.Name.StartsWith('\'') || sheet.Name.EndsWith('\'') || !names.Add(sheet.Name))
        {
            throw new ArgumentException("Sheet names must be unique, at most 31 characters and contain no Excel-forbidden characters.", nameof(sheet));
        }
        if (sheet.Rows is null) { throw new ArgumentException("Sheet rows are required.", nameof(sheet)); }
        if (sheet.Rows.Count > MaximumRows) { throw new ArgumentException("A sheet exceeds Excel's row limit.", nameof(sheet)); }
    }

    private static SheetData CreateRows(IReadOnlyList<IReadOnlyList<FileContextSpreadsheetCell>> rows, CancellationToken token)
    {
        var data = new SheetData();
        foreach (var values in rows)
        {
            token.ThrowIfCancellationRequested();
            if (values is null) { throw new ArgumentException("A row must contain a cell list.", nameof(rows)); }
            if (values.Count > MaximumColumns) { throw new ArgumentException("A row exceeds Excel's column limit.", nameof(rows)); }
            data.AppendChild(new Row(values.Select(CreateCell)));
        }
        return data;
    }

    private static Cell CreateCell(FileContextSpreadsheetCell value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var count = (value.Text is null ? 0 : 1) + (value.Number is null ? 0 : 1) +
            (value.Boolean is null ? 0 : 1) + (value.Formula is null ? 0 : 1);
        if (count > 1) { throw new ArgumentException("A cell may specify only one of text, number, boolean or formula.", nameof(value)); }
        if (value.Number is { } number)
        {
            if (!double.IsFinite(number)) { throw new ArgumentException("Cell numbers must be finite.", nameof(value)); }
            return new Cell { CellValue = new CellValue(number.ToString("R", CultureInfo.InvariantCulture)), DataType = CellValues.Number };
        }
        if (value.Boolean is { } boolean) { return new Cell { CellValue = new CellValue(boolean ? "1" : "0"), DataType = CellValues.Boolean }; }
        if (value.Formula is { } formula)
        {
            if (string.IsNullOrWhiteSpace(formula)) { throw new ArgumentException("Formula is required.", nameof(value)); }
            return new Cell { CellFormula = new CellFormula(formula.TrimStart('=')) };
        }
        if (value.Text?.Length > MaximumCellTextLength) { throw new ArgumentException("Cell text exceeds Excel's 32767-character limit.", nameof(value)); }
        return new Cell { InlineString = new InlineString { Text = new Text(value.Text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve } }, DataType = CellValues.InlineString };
    }
}
