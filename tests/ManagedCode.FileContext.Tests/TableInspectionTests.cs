using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.VisualBasic.FileIO;

namespace ManagedCode.FileContext.Tests;

public sealed class TableInspectionTests
{
    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData("\t")]
    [InlineData("|")]
    public async Task Csv_counts_logical_records_and_preserves_headers(string delimiter)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true, RootPrefix = "chat" };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var csv = string.Join(delimiter, " UPC ", "", " UPC ", "\"Multi\nline\"") + "\n" +
            string.Join(delimiter, "0001", "\"a\nb\"", "\"quoted \"\"value\"\"\"", "four") + "\n" +
            string.Join(delimiter, "", "", "", "") + "\n" + string.Join(delimiter, "0002", "", "x", "five");
        Directory.CreateDirectory(Path.Combine(scope.Directory, "chat"));
        await File.WriteAllTextAsync(Path.Combine(scope.Directory, "chat", "supplier.csv"), csv);
        options.EnableWriteTools = false;
        var result = await context.Documents.GetTablesInfoAsync("supplier.csv");
        result.Delimiter.ShouldBe(delimiter);
        result.Tables.Single().Headers.ShouldBe([" UPC ", "", " UPC ", "Multi\nline"]);
        result.Tables.Single().RowCount.ShouldBe(2);
        result.Tables.Single().HeaderRow.ShouldBe(1);
        JsonSerializer.Serialize(result).ShouldNotContain("0001");
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("UPC,Model\n", 2, 0)]
    [InlineData("UPC,Model\n1,a\n2,b", 2, 2)]
    public void Csv_handles_empty_header_only_and_missing_final_newline(string csv, int columns, long rows)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = FileContextTableReader.Read(source, "file.csv");
        result.Tables.Single().Headers.Count.ShouldBe(columns);
        result.Tables.Single().RowCount.ShouldBe(rows);
        source.CanRead.ShouldBeTrue();
    }

    [Fact]
    public async Task Workbook_uses_actual_rows_not_declared_dimension_and_retains_blank_columns()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var file = await context.Documents.CreateWorkbookAsync("supplier.xlsx", new([
            new("Products", [[new(), new(Text: "UPC"), new(), new(Text: "Model")],
                [new(), new(Text: "001"), new(), new(Text: "FT0008")], [],
                [new(), new(Text: "002")]]), new("Empty", [])]));
        var result = await context.Documents.GetTablesInfoAsync(file.Path);
        result.Tables[0].Headers.ShouldBe(["", "UPC", "", "Model"]);
        result.Tables[0].RowCount.ShouldBe(2);
        result.Tables[1].Headers.ShouldBeEmpty();
        result.Tables[1].HeaderRow.ShouldBeNull();
        result.Tables[1].RowCount.ShouldBe(0);
        await Should.ThrowAsync<InvalidDataException>(() => context.Documents.GetTablesInfoAsync(file.Path, headerRow: 100));
    }

    [Fact]
    public async Task Named_excel_tables_exclude_totals_and_keep_offsets()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var file = await context.Documents.CreateWorkbookAsync("named.xlsx", new([new("Data", [
            [new(Text: "Outside")], [new(), new(Text: "UPC"), new(Text: "Qty")],
            [new(), new(Text: "001"), new(Number: 4)], [new(), new(Text: "Total"), new(Number: 4)]])]));
        var physical = Path.Combine(scope.Directory, file.Path);
        using (var workbook = SpreadsheetDocument.Open(physical, true))
        {
            var sheet = workbook.WorkbookPart!.WorksheetParts.Single();
            var definition = sheet.AddNewPart<TableDefinitionPart>();
            definition.Table = new Table
            { Id = 1, Name = "Products", DisplayName = "Products", Reference = "B2:C4", TotalsRowCount = 1 };
            definition.Table.AppendChild(new TableColumns(
                new TableColumn { Id = 1, Name = "UPC" }, new TableColumn { Id = 2, Name = "Qty" }));
            definition.Table.Save();
        }
        var result = (await context.Documents.GetTablesInfoAsync(file.Path)).Tables.Single();
        result.Name.ShouldBe("Products");
        result.Sheet.ShouldBe("Data");
        result.StartColumn.ShouldBe(2);
        result.HeaderRow.ShouldBe(2);
        result.Headers.ShouldBe(["UPC", "Qty"]);
        result.RowCount.ShouldBe(1);
    }

    [Fact]
    public void Csv_header_override_bom_and_ragged_rows_are_explicit()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Title\nA;B\nx;y;z\n")).ToArray());
        var info = FileContextTableReader.Read(stream, "input.csv", headerRow: 2).Tables.Single();
        info.Headers.ShouldBe(["A", "B", ""]);
        info.HeaderRow.ShouldBe(2);
        info.RowCount.ShouldBe(1);
        Should.Throw<ArgumentException>(() => FileContextTableReader.Read(stream, "input.csv", headerRow: 10));
        Should.Throw<ArgumentException>(() => FileContextTableReader.Read(stream, "input.csv", delimiter: "::"));
        Should.Throw<ArgumentException>(() => FileContextTableReader.Read(stream, "input.pdf"));
        Should.Throw<ArgumentOutOfRangeException>(() => FileContextTableReader.Read(stream, "input.csv", headerRow: 0));
    }

    [Fact]
    public void Malformed_csv_cancellation_and_budgets_fail_without_partial_metadata()
    {
        using var malformed = new MemoryStream(Encoding.UTF8.GetBytes("A,B\n\"unterminated"));
        Should.Throw<MalformedLineException>(() => FileContextTableReader.Read(malformed, "file.csv"));
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("A,B\n1,2"));
        Should.Throw<OperationCanceledException>(() => FileContextTableReader.Read(source, "file.csv", cancellationToken: new(true)));
        Should.Throw<IOException>(() => FileContextTableReader.Read(source, "file.csv", options: new() { MaximumFullReadBytes = 1 }));
        Should.Throw<IOException>(() => FileContextTableReader.Read(source, "file.csv", options: new() { MaximumRangeReadBytes = 1 }));
    }
}
