using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ManagedCode.FileContext;

internal static class FileContextWorkbookReader
{
    private const int AlphabetLength = 26;
    private const int MaximumRows = 1_048_576;
    private const int MaximumColumns = 16_384;

    public static FileContextWorkbookInfo Inspect(SpreadsheetDocument document, string path, CancellationToken token)
    {
        var workbook = RequireWorkbook(document);
        var sheets = workbook.Workbook?.Sheets?.Elements<Sheet>() ?? [];
        var result = new List<FileContextWorksheetInfo>();
        foreach (var sheet in sheets)
        {
            token.ThrowIfCancellationRequested();
            var dimension = workbook.GetPartById(sheet.Id!) is WorksheetPart worksheet
                ? ReadDimension(worksheet, token)
                : null;
            result.Add(new(sheet.Name?.Value ?? string.Empty, sheet.State?.InnerText ?? "visible", dimension));
        }
        return new(path, result);
    }

    private static string? ReadDimension(WorksheetPart worksheet, CancellationToken token)
    {
        using var reader = OpenXmlReader.Create(worksheet);
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (!reader.IsStartElement) { continue; }
            if (reader.ElementType == typeof(SheetDimension))
            {
                return ((SheetDimension)reader.LoadCurrentElement()!).Reference?.Value;
            }
            if (reader.ElementType == typeof(SheetData)) { break; }
        }
        return null;
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
        var strings = new FileContextSharedStrings(workbook.SharedStringTablePart, token,
            singleCell: rowCount == 1 && columnCount == 1);
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

    private static List<FileContextWorkbookCell> ReadCells(WorksheetPart worksheet, FileContextSharedStrings strings,
        int startRow, int rowCount, int startColumn, int columnCount, long byteLimit, CancellationToken token)
    {
        using var reader = OpenXmlReader.Create(worksheet);
        var cells = new List<FileContextWorkbookCell>();
        if (!MoveToFirstRow(reader, token)) { return cells; }

        long bytes = 0;
        var rowNumber = 0;
        do
        {
            token.ThrowIfCancellationRequested();
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement) { continue; }
            rowNumber = ReadRowNumber(reader, rowNumber);
            if (rowNumber < startRow || rowNumber >= startRow + rowCount) { continue; }
            var row = (Row)reader.LoadCurrentElement()!;
            var column = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                token.ThrowIfCancellationRequested();
                column = cell.CellReference is null ? column + 1 : ReadColumn(cell.CellReference.Value!);
                if (column < startColumn || column >= startColumn + columnCount) { continue; }
                var value = ReadCell(cell, strings, $"{ColumnName(column)}{rowNumber}");
                bytes += (long)Encoding.UTF8.GetByteCount(value.Value) + Encoding.UTF8.GetByteCount(value.Formula ?? string.Empty) + value.Address.Length + value.Type.Length;
                if (bytes > byteLimit) { throw new IOException("Workbook range exceeds the configured read budget. Request a smaller range."); }
                cells.Add(value);
            }
        }
        while (reader.ReadNextSibling());
        return cells;
    }

    private static bool MoveToFirstRow(OpenXmlReader reader, CancellationToken token)
    {
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.IsStartElement && reader.ElementType == typeof(SheetData))
            {
                return reader.ReadFirstChild();
            }
        }
        return false;
    }

    internal static int ReadRowNumber(OpenXmlReader reader, int previous)
    {
        var attributes = reader.Attributes;
        for (var index = 0; index < attributes.Count; index++)
        {
            var attribute = attributes[index];
            if (string.IsNullOrEmpty(attribute.NamespaceUri) && string.Equals(attribute.LocalName, "r", StringComparison.Ordinal))
            {
                return checked((int)XmlConvert.ToUInt32(attribute.Value!));
            }
        }
        return previous + 1;
    }

    private static FileContextWorkbookCell ReadCell(Cell cell, FileContextSharedStrings strings, string address)
    {
        var type = cell.DataType?.Value;
        var value = ReadCellValue(cell, strings, type);
        var kind = "number";
        if (type == CellValues.SharedString || type == CellValues.InlineString || type == CellValues.String) { kind = "text"; }
        else if (type == CellValues.Boolean) { kind = "boolean"; }
        else if (type == CellValues.Error) { kind = "error"; }
        else if (type == CellValues.Date) { kind = "date"; }
        else if (value.Length == 0) { kind = "blank"; }
        return new(address, kind, value, cell.CellFormula?.Text);
    }

    internal static string ReadCellValue(Cell cell, FileContextSharedStrings strings, CellValues? type)
    {
        if (type == CellValues.InlineString) { return ReadBaseText(cell.InlineString); }
        var value = cell.CellValue?.Text ?? string.Empty;
        if (type != CellValues.SharedString) { return value; }
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        { throw FileContextSharedStrings.InvalidReference(); }
        return strings.Get(index);
    }

    internal static string ReadBaseText(OpenXmlElement? value)
    {
        if (value is null) { return string.Empty; }
        if (value.FirstChild is Text text && ReferenceEquals(text, value.LastChild)) { return text.Text; }
        return string.Concat(value.Descendants<Text>()
            .Where(static text => text.Parent is not PhoneticRun).Select(static text => text.Text));
    }

    private static WorkbookPart RequireWorkbook(SpreadsheetDocument document) =>
        document.WorkbookPart ?? throw new InvalidDataException("The package has no Excel workbook.");

    internal static int ReadColumn(string address)
    {
        var result = 0;
        foreach (var character in address)
        {
            if (!char.IsAsciiLetter(character)) { break; }
            result = checked(result * AlphabetLength + char.ToUpperInvariant(character) - 'A' + 1);
        }
        return result;
    }

    private static string ColumnName(int column)
    {
        Span<char> result = stackalloc char[3]; // Excel's last column is XFD.
        var start = result.Length;
        while (column > 0)
        {
            column--;
            result[--start] = (char)('A' + column % AlphabetLength);
            column /= AlphabetLength;
        }
        return new string(result[start..]);
    }
}
