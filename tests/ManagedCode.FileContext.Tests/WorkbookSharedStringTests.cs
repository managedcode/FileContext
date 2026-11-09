using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ManagedCode.FileContext.Tests;

public sealed class WorkbookSharedStringTests
{
    [Fact]
    public async Task Numeric_range_skips_unused_shared_strings_but_their_part_limit_still_applies_when_selected()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true, MaximumOpenXmlPartCharacters = 5_000 };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var created = await context.Documents.CreateWorkbookAsync("mixed.xlsx", new([new("Data", [[new(Text: "shared"), new(Number: 42)]])]));
        AddOversizedSharedString(scope, created.Path);

        var numeric = await context.Documents.ReadWorkbookRangeAsync(created.Path, "Data", 1, 1, 2, 1);
        numeric.Cells.ShouldHaveSingleItem().Value.ShouldBe("42");

        await Should.ThrowAsync<XmlException>(() => context.Documents.ReadWorkbookRangeAsync(created.Path, "Data", 1, 1, 1, 1));
    }

    private static void AddOversizedSharedString(TestStorageScope scope, string path)
    {
        using var document = SpreadsheetDocument.Open(Path.Combine(scope.Directory, path), true);
        var workbook = document.WorkbookPart!;
        var part = workbook.AddNewPart<SharedStringTablePart>();
        part.SharedStringTable = new SharedStringTable();
        AppendSharedString(part.SharedStringTable, "shared");
        AppendSharedString(part.SharedStringTable, new string('x', 12_000));
        var cell = workbook.WorksheetParts.Single().Worksheet!.Descendants<Cell>().First();
        SetSharedString(cell, "0");
    }

    [Fact]
    public async Task Single_cell_range_preserves_empty_shared_strings_and_repeated_coordinates()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var created = await context.Documents.CreateWorkbookAsync("repeated.xlsx", new([new("Data", [[new(Text: "shared")]])]));

        using (var document = SpreadsheetDocument.Open(Path.Combine(scope.Directory, created.Path), true))
        {
            var workbook = document.WorkbookPart!;
            var part = workbook.AddNewPart<SharedStringTablePart>();
            part.SharedStringTable = new SharedStringTable();
            AppendSharedString(part.SharedStringTable, "first");
            part.SharedStringTable.AppendChild(new SharedStringItem());
            var row = workbook.WorksheetParts.Single().Worksheet!.Descendants<Row>().Single();
            SetSharedString(row.Elements<Cell>().Single(), "1");
            SetSharedString(row.AppendChild(new Cell { CellReference = "A1" }), "0");
            SetSharedString(row.AppendChild(new Cell { CellReference = "A1" }), "1");
            SetSharedString(row.AppendChild(new Cell { CellReference = "B1" }), "4");
        }

        var result = await context.Documents.ReadWorkbookRangeAsync(created.Path, "Data", 1, 1, 1, 1);
        result.Cells.Select(cell => cell.Value).ShouldBe(["", "first", ""]);
        await Should.ThrowAsync<InvalidDataException>(() => context.Documents.ReadWorkbookRangeAsync(created.Path, "Data", 1, 1, 2, 1));
    }

    private static void AppendSharedString(SharedStringTable table, string text)
    {
        var item = table.AppendChild(new SharedStringItem());
        item.AppendChild(new Text(text));
    }

    private static void SetSharedString(Cell cell, string index)
    {
        cell.InlineString = null;
        cell.DataType = CellValues.SharedString;
        cell.CellValue = new CellValue(index);
    }
}
