using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ManagedCode.FileContext;

internal static class FileContextWorkbookReader
{
    private const int AlphabetLength = 26;
    private const int MaximumRows = 1_048_576;
    private const int MaximumColumns = 16_384;

    public static FileContextWorkbookInfo Inspect(SpreadsheetDocument document, string path)
    {
        var workbook = RequireWorkbook(document);
        var sheets = workbook.Workbook?.Sheets?.Elements<Sheet>() ?? [];
        return new(path, sheets.Select(sheet => new FileContextWorksheetInfo(
            sheet.Name?.Value ?? string.Empty,
            sheet.State?.InnerText ?? "visible",
            ((WorksheetPart)workbook.GetPartById(sheet.Id!)).Worksheet?.GetFirstChild<SheetDimension>()?.Reference?.Value)).ToArray());
    }

    public static FileContextWorkbookRange Read(SpreadsheetDocument document, string path, string sheetName,
        int startRow, int rowCount, int startColumn, int columnCount, long byteLimit, CancellationToken token)
    {
        ValidateRange(startRow, rowCount, startColumn, columnCount);
        var workbook = RequireWorkbook(document);
        var sheet = workbook.Workbook?.Sheets?.Elements<Sheet>().SingleOrDefault(item =>
            string.Equals(item.Name?.Value, sheetName, StringComparison.Ordinal))
            ?? throw new ArgumentException("The requested worksheet does not exist.", nameof(sheetName));
        var worksheet = workbook.GetPartById(sheet.Id!) as WorksheetPart
            ?? throw new InvalidDataException("The requested sheet is not a worksheet.");
        var strings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>()
            .Select(static item => string.Concat(item.Descendants<Text>().Select(text => text.Text))).ToArray() ?? [];
        var cells = ReadCells(worksheet, strings, startRow, rowCount, startColumn, columnCount, byteLimit, token);
        var range = $"{ColumnName(startColumn)}{startRow}:{ColumnName(startColumn + columnCount - 1)}{startRow + rowCount - 1}";
        return new(path, sheetName, range, cells);
    }

    public static void ValidateRange(int startRow, int rowCount, int startColumn, int columnCount)
    {
        if (startRow < 1 || rowCount < 1 || (long)startRow + rowCount - 1 > MaximumRows)
        { throw new ArgumentOutOfRangeException(nameof(rowCount), "The range must fit Excel's 1048576 rows, starting at one."); }
        if (startColumn < 1 || columnCount < 1 || (long)startColumn + columnCount - 1 > MaximumColumns)
        { throw new ArgumentOutOfRangeException(nameof(columnCount), "The range must fit Excel's 16384 columns, starting at one."); }
    }

    private static List<FileContextWorkbookCell> ReadCells(WorksheetPart worksheet, string[] strings,
        int startRow, int rowCount, int startColumn, int columnCount, long byteLimit, CancellationToken token)
    {
        using var reader = OpenXmlReader.Create(worksheet);
        var cells = new List<FileContextWorkbookCell>();
        long bytes = 0;
        var rowNumber = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement) { continue; }
            var row = (Row)reader.LoadCurrentElement()!;
            rowNumber = row.RowIndex is null ? rowNumber + 1 : checked((int)row.RowIndex.Value);
            if (rowNumber < startRow || rowNumber >= startRow + rowCount) { continue; }
            var column = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                token.ThrowIfCancellationRequested();
                column = cell.CellReference is null ? column + 1 : ReadColumn(cell.CellReference.Value!);
                if (column < startColumn || column >= startColumn + columnCount) { continue; }
                var value = ReadCell(cell, strings, $"{ColumnName(column)}{rowNumber}");
                bytes += Encoding.UTF8.GetByteCount(value.Value) + Encoding.UTF8.GetByteCount(value.Formula ?? string.Empty) + value.Address.Length + value.Type.Length;
                if (bytes > byteLimit) { throw new IOException("Workbook range exceeds the configured read budget. Request a smaller range."); }
                cells.Add(value);
            }
        }
        return cells;
    }

    internal static FileContextWorkbookCell ReadCell(Cell cell, string[] strings, string address)
    {
        var type = cell.DataType?.Value;
        var value = cell.CellValue?.Text ?? string.Empty;
        if (type == CellValues.SharedString)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= strings.Length)
            { throw new InvalidDataException("Workbook cell contains an invalid shared-string reference."); }
            value = strings[index];
        }
        if (type == CellValues.InlineString)
        { value = string.Concat(cell.InlineString?.Descendants<Text>().Select(static text => text.Text) ?? []); }
        var kind = "number";
        if (type == CellValues.SharedString || type == CellValues.InlineString || type == CellValues.String) { kind = "text"; }
        else if (type == CellValues.Boolean) { kind = "boolean"; }
        else if (type == CellValues.Error) { kind = "error"; }
        else if (type == CellValues.Date) { kind = "date"; }
        else if (value.Length == 0) { kind = "blank"; }
        return new(address, kind, value, cell.CellFormula?.Text);
    }

    private static WorkbookPart RequireWorkbook(SpreadsheetDocument document) =>
        document.WorkbookPart ?? throw new InvalidDataException("The package has no Excel workbook.");

    internal static int ReadColumn(string address)
    {
        var result = 0;
        foreach (var character in address.TakeWhile(char.IsAsciiLetter))
        { result = checked(result * AlphabetLength + char.ToUpperInvariant(character) - 'A' + 1); }
        return result;
    }

    private static string ColumnName(int column)
    {
        var result = new StringBuilder();
        while (column > 0)
        {
            column--;
            result.Insert(0, (char)('A' + column % AlphabetLength));
            column /= AlphabetLength;
        }
        return result.ToString();
    }
}
