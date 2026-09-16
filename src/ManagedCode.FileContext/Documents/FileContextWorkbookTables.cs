using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ManagedCode.FileContext;

internal static class FileContextWorkbookTables
{
    private const int MaximumRangeReferences = 2;
    private const int MaximumRows = 1_048_576;
    private const int MaximumColumns = 16_384;

    public static IReadOnlyList<FileContextTableInfo> Read(SpreadsheetDocument document, int? headerRow, CancellationToken token)
    {
        if (headerRow > MaximumRows) { throw new ArgumentOutOfRangeException(nameof(headerRow)); }
        var workbook = document.WorkbookPart ?? throw new InvalidDataException("The package has no Excel workbook.");
        var strings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>()
            .Select(static item => string.Concat(item.Descendants<Text>().Select(text => text.Text))).ToArray() ?? [];
        var result = new List<FileContextTableInfo>();
        foreach (var sheet in workbook.Workbook?.Sheets?.Elements<Sheet>() ?? [])
        {
            token.ThrowIfCancellationRequested();
            if (workbook.GetPartById(sheet.Id!) is not WorksheetPart part) { continue; }
            var name = sheet.Name?.Value ?? string.Empty;
            var tables = part.TableDefinitionParts.Select(static item => item.Table ?? throw new InvalidDataException("Excel table definition is missing.")).ToArray();
            if (tables.Length == 0)
            { result.Add(Inspect(part, strings, new(name, name, headerRow), token)); }
            foreach (var table in tables)
            { result.Add(Inspect(part, strings, NamedTable(table, name), token)); }
        }
        return result;
    }

    private static TableBounds NamedTable(Table table, string sheet)
    {
        var range = table.Reference?.Value?.Split(':') ?? throw new InvalidDataException("Excel table has no cell range.");
        if (range.Length is < 1 or > MaximumRangeReferences) { throw new InvalidDataException("Excel table range must contain one or two cell references."); }
        var startColumn = FileContextWorkbookReader.ReadColumn(range[0]);
        var endColumn = FileContextWorkbookReader.ReadColumn(range[^1]);
        var first = RowNumber(range[0]);
        var last = RowNumber(range[^1]);
        FileContextWorkbookReader.ValidateRange(first, last - first + 1, startColumn, endColumn - startColumn + 1);
        var hasHeader = (table.HeaderRowCount?.Value ?? 1) > 0;
        return new(table.Name?.Value ?? sheet, sheet, hasHeader ? first : null,
            startColumn, endColumn, first, last - checked((int)(table.TotalsRowCount?.Value ?? 0)),
            table.TableColumns?.Elements<TableColumn>().Select(static column => column.Name?.Value ?? string.Empty).ToArray() ?? []);
    }

    private static FileContextTableInfo Inspect(WorksheetPart part, string[] strings, TableBounds bounds, CancellationToken token)
    {
        var accumulator = new TableAccumulator(bounds);
        using var reader = OpenXmlReader.Create(part);
        var rowNumber = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement) { continue; }
            var row = (Row)reader.LoadCurrentElement()!;
            rowNumber = row.RowIndex is null ? rowNumber + 1 : checked((int)row.RowIndex.Value);
            if (rowNumber < bounds.FirstRow || rowNumber > bounds.LastRow) { continue; }
            var cells = new Dictionary<int, string>();
            var hasData = false;
            var column = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                token.ThrowIfCancellationRequested();
                column = cell.CellReference is null ? column + 1 : FileContextWorkbookReader.ReadColumn(cell.CellReference.Value!);
                if (column < bounds.StartColumn || column > bounds.EndColumn) { continue; }
                var value = FileContextWorkbookReader.ReadCell(cell, strings, string.Empty);
                hasData |= value.Value.Length > 0 || value.Formula is not null;
                if (value.Value.Length > 0 || value.Formula is not null || cell.InlineString is not null || cell.CellValue is not null)
                { cells[column] = value.Value; }
            }
            accumulator.Add(rowNumber, cells, hasData);
        }
        return accumulator.Result();
    }

    private static int RowNumber(string address) => int.Parse(
        new string(address.Where(char.IsAsciiDigit).ToArray()), CultureInfo.InvariantCulture);

    private sealed record TableBounds(string Name, string Sheet, int? HeaderRow,
        int StartColumn = 1, int EndColumn = MaximumColumns, int FirstRow = 1, int LastRow = MaximumRows, string[]? Names = null);

    private sealed class TableAccumulator(TableBounds bounds)
    {
        private int? headerRow = bounds.HeaderRow;
        private bool foundHeader;
        private readonly Dictionary<int, string> headers = [];
        private int lastColumn = bounds.Names is null ? bounds.StartColumn - 1 : bounds.EndColumn;
        private long rows;

        public void Add(int row, Dictionary<int, string> cells, bool hasData)
        {
            if (headerRow.HasValue && row < headerRow) { return; }
            if (bounds.Names is null && !headerRow.HasValue && hasData) { headerRow = row; }
            if (row == headerRow)
            {
                foundHeader = true;
                foreach (var (column, value) in cells) { headers[column] = value; }
            }
            else if ((headerRow.HasValue || bounds.Names is not null) && hasData) { rows++; }
            if (cells.Count > 0 && (hasData || row == headerRow)) { lastColumn = Math.Max(lastColumn, cells.Keys.Max()); }
        }

        public FileContextTableInfo Result()
        {
            if (bounds.Names is null && bounds.HeaderRow.HasValue && !foundHeader)
            { throw new InvalidDataException("The requested worksheet header row does not exist."); }
            var names = Enumerable.Range(bounds.StartColumn, lastColumn - bounds.StartColumn + 1)
                .Select(column => bounds.Names is not null
                    ? bounds.Names.ElementAtOrDefault(column - bounds.StartColumn) ?? string.Empty
                    : headers.GetValueOrDefault(column, string.Empty)).ToArray();
            return new(bounds.Name, bounds.Sheet, headerRow, bounds.StartColumn, names, rows);
        }
    }
}
