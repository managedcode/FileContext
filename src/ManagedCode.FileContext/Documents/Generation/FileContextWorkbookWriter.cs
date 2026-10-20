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

    public static byte[] Create(FileContextWorkbook workbook, byte[]? template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (workbook.Sheets is null) { throw new ArgumentException("Workbook sheets are required.", nameof(workbook)); }
        if (workbook.Sheets.Count == 0) { throw new ArgumentException("A workbook needs at least one sheet.", nameof(workbook)); }
        using var stream = new MemoryStream();
        if (template is not null) { stream.Write(template); stream.Position = 0; }
        using (var document = template is null ? SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook) : SpreadsheetDocument.Open(stream, true))
        {
            if (document.DocumentType != SpreadsheetDocumentType.Workbook)
            { throw new ArgumentException("Template must be a macro-free XLSX workbook.", nameof(template)); }
            var part = document.WorkbookPart ?? document.AddWorkbookPart();
            part.Workbook ??= new Workbook { Sheets = new Sheets() };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var styles = new FileContextWorkbookStyles(part);
            foreach (var sheet in workbook.Sheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateSheet(sheet, names);
                WriteSheet(part, sheet, styles, cancellationToken);
            }
            part.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static void WriteSheet(WorkbookPart part, FileContextWorksheet sheet, FileContextWorkbookStyles styles, CancellationToken token)
    {
        if (sheet.Style is { } requestedStyle &&
            (requestedStyle.NumberFormats is null || requestedStyle.ColumnWidths is null))
        { throw new ArgumentException("Style numberFormats and columnWidths must be objects.", nameof(sheet)); }
        var sheets = part.Workbook!.GetFirstChild<Sheets>() ?? part.Workbook.AppendChild(new Sheets());
        var existing = sheets.Elements<Sheet>().FirstOrDefault(item => string.Equals(item.Name, sheet.Name, StringComparison.OrdinalIgnoreCase));
        var worksheet = existing is null ? part.AddNewPart<WorksheetPart>() : (WorksheetPart)part.GetPartById(existing.Id!);
        worksheet.Worksheet ??= new Worksheet();
        var prior = worksheet.Worksheet.GetFirstChild<SheetData>();
        var data = CreateRows(sheet.Rows, sheet.Style, styles, token);
        if (prior is null) { worksheet.Worksheet.AppendChild(data); }
        else
        {
            InheritStyles(data, prior);
            worksheet.Worksheet.ReplaceChild(data, prior);
        }
        if (sheet.Style is { } style) { FileContextWorkbookStyles.Layout(worksheet.Worksheet, style); }
        if (existing is null)
        {
            var nextId = sheets.Elements<Sheet>().Select(item => item.SheetId!.Value).DefaultIfEmpty(0U).Max() + 1;
            sheets.AppendChild(new Sheet { Id = part.GetIdOfPart(worksheet), SheetId = nextId, Name = sheet.Name });
        }
        worksheet.Worksheet.Save();
    }

    private static void InheritStyles(SheetData data, SheetData prior)
    {
        var inherited = new Dictionary<(uint Row, int Column), uint>();
        uint rowNumber = 0;
        foreach (var row in prior.Elements<Row>())
        {
            rowNumber = row.RowIndex?.Value ?? rowNumber + 1;
            var column = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                column = cell.CellReference is null ? column + 1 : FileContextWorkbookReader.ReadColumn(cell.CellReference.Value!);
                if (cell.StyleIndex is { } style) { inherited[(rowNumber, column)] = style.Value; }
            }
        }
        rowNumber = 0;
        foreach (var row in data.Elements<Row>())
        {
            rowNumber++;
            var column = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                column++;
                if (cell.StyleIndex is null && inherited.TryGetValue((rowNumber, column), out var style))
                { cell.StyleIndex = style; }
            }
        }
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

    private static SheetData CreateRows(IReadOnlyList<IReadOnlyList<FileContextSpreadsheetCell>> rows, FileContextWorksheetStyle? style, FileContextWorkbookStyles styles, CancellationToken token)
    {
        var data = new SheetData();
        var rowIndex = 0;
        foreach (var values in rows)
        {
            token.ThrowIfCancellationRequested();
            if (values is null) { throw new ArgumentException("A row must contain a cell list.", nameof(rows)); }
            if (values.Count > MaximumColumns) { throw new ArgumentException("A row exceeds Excel's column limit.", nameof(rows)); }
            var row = new Row();
            for (var column = 0; column < values.Count; column++)
            {
                var cell = CreateCell(values[column]);
                if (style is not null && styles.Style(style, rowIndex, column + 1) is var index && index != 0)
                { cell.StyleIndex = index; }
                row.AppendChild(cell);
            }
            data.AppendChild(row);
            rowIndex++;
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
