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
        var options = new FileContextOptions { EnableWriteTools = true, MaximumOpenXmlPartCharacters = 2_048 };
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
        var item = part.SharedStringTable.AppendChild(new SharedStringItem());
        item.AppendChild(new Text(new string('x', 8_192)));
        var cell = workbook.WorksheetParts.Single().Worksheet!.Descendants<Cell>().First();
        cell.InlineString = null;
        cell.DataType = CellValues.SharedString;
        cell.CellValue = new CellValue("0");
    }
}
