using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ManagedCode.FileContext.Tests;

public sealed class WorkbookModuleEdgeTests
{
    [Fact]
    public async Task Disjoint_named_tables_preserve_header_modes_totals_and_implicit_row_coordinates()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var file = await context.Documents.CreateWorkbookAsync("named.xlsx", new([new("Data", [
            [new(Text: "unrelated")], [new(), new(Text: "Item")], [new(), new(Text: "A")],
            [new(), new(Text: "Total")], [new(Text: "separator")], [new(), new(Text: "B")], [new(), new(Text: "C")]])]));
        using (var document = SpreadsheetDocument.Open(Path.Combine(scope.Directory, file.Path), true))
        {
            var worksheet = document.WorkbookPart!.WorksheetParts.Single();
            AddTable(worksheet, 1, "WithHeader", "B2:B4", headerRows: 1, totalsRows: 1);
            AddTable(worksheet, 2, "NoHeader", "B6:B7", headerRows: 0, totalsRows: 0);
            worksheet.Worksheet!.Descendants<Row>().Last().RowIndex = null;
        }

        var tables = (await context.Documents.GetTablesInfoAsync(file.Path)).Tables;
        var withHeader = tables.Single(table => string.Equals(table.Name, "WithHeader", StringComparison.Ordinal));
        (withHeader.HeaderRow, withHeader.StartColumn, withHeader.RowCount).ShouldBe((2, 2, 1L));
        withHeader.Headers.ShouldBe(["Column"]);
        var withoutHeader = tables.Single(table => string.Equals(table.Name, "NoHeader", StringComparison.Ordinal));
        (withoutHeader.HeaderRow, withoutHeader.StartColumn, withoutHeader.RowCount).ShouldBe((null, 2, 2L));
        withoutHeader.Headers.ShouldBe(["Column"]);
    }

    [Fact]
    public async Task Single_cell_window_reaches_the_last_valid_excel_coordinate()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var file = await context.Documents.CreateWorkbookAsync("last.xlsx", new([new("Data", [[new(Text: "Кінець")]])]));
        using (var document = SpreadsheetDocument.Open(Path.Combine(scope.Directory, file.Path), true))
        {
            var row = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Row>().Single();
            row.RowIndex = 1_048_576;
            row.Elements<Cell>().Single().CellReference = "XFD1048576";
        }

        var result = await context.Documents.ReadWorkbookRangeAsync(file.Path, "Data", 1_048_576, 1, 16_384, 1);
        result.Range.ShouldBe("XFD1048576:XFD1048576");
        result.Cells.ShouldHaveSingleItem().Value.ShouldBe("Кінець");
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            context.Documents.ReadWorkbookRangeAsync(file.Path, "Data", 1_048_576, 2, 16_384, 1));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            context.Documents.ReadWorkbookRangeAsync(file.Path, "Data", 1_048_576, 1, 16_384, 2));
    }

    private static void AddTable(WorksheetPart worksheet, uint id, string name, string range, uint headerRows, uint totalsRows)
    {
        var definition = worksheet.AddNewPart<TableDefinitionPart>();
        definition.Table = new Table
        {
            Id = id,
            Name = name,
            DisplayName = name,
            Reference = range,
            HeaderRowCount = headerRows,
            TotalsRowCount = totalsRows
        };
        var columns = definition.Table.AppendChild(new TableColumns());
        columns.AppendChild(new TableColumn { Id = 1, Name = "Column" });
    }
}
