using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace ManagedCode.FileContext.Tests;

public sealed class WorkbookFormattingTests
{
    [Fact]
    public async Task Styled_workbook_preserves_values_formulas_formats_widths_and_frozen_header()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "exec", EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var files = new FileContextService(store, options);
        var sheet = new FileContextWorksheet("Results", [
            [new(Text: "Revenue"), new(Text: "Margin"), new(Text: "As of")],
            [new(Number: 123.45), new(Formula: "A2/1000"), new(Number: 45292)]])
        {
            Style = new()
            {
                BoldHeader = true,
                HeaderFill = "#17365D",
                FreezeHeader = true,
                NumberFormats = new Dictionary<int, string> { [1] = "$#,##0.00", [2] = "0.0%", [3] = "yyyy-mm-dd" },
                ColumnWidths = new Dictionary<int, double> { [1] = 24, [2] = 18, [3] = 16 }
            }
        };
        var created = await files.Documents.CreateWorkbookAsync("report.xlsx", new([sheet]));
        await using var stream = await store.OpenReadAsync(created.Path);
        using var document = SpreadsheetDocument.Open(stream, false);
        new OpenXmlValidator().Validate(document).ShouldBeEmpty();
        var part = document.WorkbookPart!;
        var worksheet = part.WorksheetParts.Single().Worksheet!;
        worksheet.GetFirstChild<SheetViews>()!.Descendants<Pane>().Single().TopLeftCell!.Value.ShouldBe("A2");
        worksheet.GetFirstChild<Columns>()!.Elements<Column>().First().Width!.Value.ShouldBe(24);
        var rows = worksheet.GetFirstChild<SheetData>()!.Elements<Row>().ToArray();
        var header = rows[0].Elements<Cell>().First();
        var style = part.WorkbookStylesPart!.Stylesheet!;
        var headerStyle = style.CellFormats!.Elements<CellFormat>().ElementAt((int)header.StyleIndex!.Value);
        style.Fonts!.Elements<Font>().ElementAt((int)headerStyle.FontId!.Value).Bold.ShouldNotBeNull();
        style.Fills!.Elements<Fill>().ElementAt((int)headerStyle.FillId!.Value).PatternFill!.ForegroundColor!.Rgb!.Value.ShouldBe("FF17365D");
        var values = rows[1].Elements<Cell>().ToArray();
        values[0].CellValue!.Text.ShouldBe("123.45");
        values[1].CellFormula!.Text.ShouldBe("A2/1000");
        style.NumberingFormats!.Elements<NumberingFormat>().Select(item => item.FormatCode!.Value)
            .ShouldBe(["$#,##0.00", "0.0%", "yyyy-mm-dd"]);
    }

    [Fact]
    public async Task Scoped_template_retains_styles_layout_and_untouched_sheets()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "exec", EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var files = new FileContextService(store, options);
        var sheet = new FileContextWorksheet("Results", [[new(Text: "Template")]])
        { Style = new() { BoldHeader = true, HeaderFill = "#17365D", FreezeHeader = true, ColumnWidths = new Dictionary<int, double> { [1] = 30 } } };
        var template = await files.Documents.CreateWorkbookAsync("template.xlsx", new([sheet, new("Notes", [[new(Text: "Keep")]])]));
        var created = await files.Documents.CreateWorkbookAsync("result.xlsx", new([new("Results", [[new(Text: "New result")]]) { Style = new() { ColumnWidths = new Dictionary<int, double> { [1] = 35 } } }]) { TemplatePath = template.Path });
        await using var stream = await store.OpenReadAsync(created.Path);
        using var document = SpreadsheetDocument.Open(stream, false);
        new OpenXmlValidator().Validate(document).ShouldBeEmpty();
        document.WorkbookPart!.WorksheetParts.Count().ShouldBe(2);
        var data = document.WorkbookPart.WorksheetParts.Select(part => part.Worksheet!).Single(item => item.GetFirstChild<SheetViews>() is not null);
        data.Descendants<Cell>().Single().InlineString!.InnerText.ShouldBe("New result");
        data.Descendants<Cell>().Single().StyleIndex!.Value.ShouldBeGreaterThan(0U);
        data.GetFirstChild<Columns>()!.Elements<Column>().Single().Width!.Value.ShouldBe(35);
        (await files.Documents.ReadWorkbookRangeAsync(created.Path, "Notes", 1, 1, 1, 1)).Cells.Single().Value.ShouldBe("Keep");
        await Should.ThrowAsync<ArgumentException>(() => files.Documents.CreateWorkbookAsync("escape.xlsx", new([sheet]) { TemplatePath = "../other/template.xlsx" }));
    }

    [Fact]
    public async Task Sparse_template_style_is_inherited_by_coordinate_instead_of_cell_position()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "exec", EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var files = new FileContextService(store, options);
        var sheet = new FileContextWorksheet("Results", [[new(Text: "Header")]]) { Style = new() { BoldHeader = true } };
        var original = await files.Documents.CreateWorkbookAsync("original.xlsx", new([sheet]));
        using var template = new MemoryStream();
        await using (var source = await store.OpenReadAsync(original.Path)) { await source.CopyToAsync(template); }
        template.Position = 0;
        using (var document = SpreadsheetDocument.Open(template, true))
        {
            var row = document.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Row>().Single();
            row.RowIndex = 2;
            row.Elements<Cell>().Single().CellReference = "B2";
            document.WorkbookPart.WorksheetParts.Single().Worksheet!.Save();
        }
        template.Position = 0;
        var saved = await store.CreateFileAsync("sparse.xlsx", template, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var result = await files.Documents.CreateWorkbookAsync("result.xlsx", new([new("Results", [
            [new(Text: "A1"), new(Text: "B1")], [new(Text: "A2"), new(Text: "B2")]])])
        { TemplatePath = saved.Path });
        await using var stream = await store.OpenReadAsync(result.Path);
        using var output = SpreadsheetDocument.Open(stream, false);
        var cells = output.WorkbookPart!.WorksheetParts.Single().Worksheet!.Descendants<Cell>().ToArray();
        cells[0].StyleIndex.ShouldBeNull();
        cells[3].StyleIndex!.Value.ShouldBeGreaterThan(0U);
    }

    [Theory]
    [InlineData("red", 1, 20)]
    [InlineData("#17365D", 0, 20)]
    [InlineData("#17365D", 1, 256)]
    public async Task Invalid_style_is_rejected_without_creating_a_file(string fill, int column, double width)
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var files = new FileContextService(new ManagedCodeStorageFileStore(storage.Storage, options), options);
        var sheet = new FileContextWorksheet("Results", [[new(Text: "Header")]])
        { Style = new() { HeaderFill = fill, ColumnWidths = new Dictionary<int, double> { [column] = width } } };
        await Should.ThrowAsync<ArgumentException>(() => files.Documents.CreateWorkbookAsync("invalid.xlsx", new([sheet])));
        (await files.ListFilesAsync()).ShouldBeEmpty();
    }
}
