using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.AI;

namespace ManagedCode.FileContext.Tests;

public sealed class WorkbookReadingTests
{
    [Fact]
    public async Task Native_ranges_preserve_sparse_coordinates_shared_strings_precision_and_cached_formulas()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true, RootPrefix = "chat" };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        var created = await context.Documents.CreateWorkbookAsync("supplier.xlsx", new([
            new("Products", [[new(Text: "EAN/UPC"), new(), new(Text: "Material")],
                [new(Text: "000012345678"), new(Number: 12.5), new(Text: "FT0008")]]),
            new("Other", [[new(Boolean: true)]])]));
        Rewrite(scope, options, created.Path);
        options.EnableWriteTools = false;
        var info = await context.Documents.GetWorkbookInfoAsync(created.Path);
        info.Sheets.Select(sheet => sheet.Name).ShouldBe(["Products", "Other"]);
        var result = await context.Documents.ReadWorkbookRangeAsync(created.Path, "Products", 2, 2, 1, 4);
        result.Range.ShouldBe("A2:D3");
        result.Cells.Select(cell => cell.Address).ShouldBe(["A2", "C2", "D2", "B3"]);
        result.Cells[0].Value.ShouldBe("000012345678");
        result.Cells[0].Type.ShouldBe("text");
        result.Cells[1].Value.ShouldBe("FT0008");
        result.Cells[2].Value.ShouldBe("123456789012345.6");
        result.Cells[2].Formula.ShouldBe("SUM(B2:C2)");
        result.Cells[3].Type.ShouldBe("error");
        result.Cells[3].Value.ShouldBe("#N/A");
        (await context.Documents.ReadWorkbookRangeAsync(created.Path, "Other", 1, 1, 1, 1)).Cells[0].Type.ShouldBe("boolean");
        (await context.Documents.ReadWorkbookRangeAsync(created.Path, "Products", 10, 1, 1, 1)).Cells.ShouldBeEmpty();
        await Should.ThrowAsync<ArgumentException>(() => context.Documents.ReadWorkbookRangeAsync(created.Path, "Missing", 1, 1, 1, 1));
    }

    [Fact]
    public async Task Native_read_tools_work_read_only_and_enforce_scope_cancellation_and_budgets()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true, RootPrefix = "chat" };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        var created = await context.Documents.CreateWorkbookAsync("input.xlsx", new([new("Data", [[new(Text: "source data")]])]));
        options.EnableWriteTools = false;
        var function = AIFunctionFactory.Create(context.Documents.ReadWorkbookRangeAsync);
        var result = await function.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)
        {
            ["path"] = created.Path,
            ["sheet"] = "Data",
            ["startRow"] = 1,
            ["rowCount"] = 1,
            ["startColumn"] = 1,
            ["columnCount"] = 1
        });
        result!.ToString()!.ShouldContain("source data");
        await Should.ThrowAsync<ArgumentException>(() => context.Documents.GetWorkbookInfoAsync("../outside.xlsx"));
        var other = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, new() { RootPrefix = "other" }));
        await Should.ThrowAsync<FileNotFoundException>(() => other.Documents.GetWorkbookInfoAsync(created.Path));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => context.Documents.ReadWorkbookRangeAsync(created.Path, "Data", 0, 1, 1, 1));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => context.Documents.ReadWorkbookRangeAsync(created.Path, "Data", 1, int.MaxValue, 1, 1));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => context.Documents.GetWorkbookInfoAsync(created.Path, cancelled.Token));
        options.MaximumRangeReadBytes = 2;
        await Should.ThrowAsync<IOException>(() => context.Documents.ReadWorkbookRangeAsync(created.Path, "Data", 1, 1, 1, 1));
        options.MaximumFullReadBytes = 2;
        await Should.ThrowAsync<IOException>(() => context.Documents.GetWorkbookInfoAsync(created.Path));
    }

    [Fact]
    public async Task Generic_text_tools_do_not_expose_workbook_bytes()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        var workbook = await context.Documents.CreateWorkbookAsync("source.XLSX", new([new("Data", [[new(Text: "source value")]])]));
        var text = await context.Documents.CreateTextAsync("notes.md", "source text");
        await Should.ThrowAsync<InvalidOperationException>(() => store.ReadAsync(workbook.Path));
        await Should.ThrowAsync<InvalidOperationException>(() => context.ReadRangeAsync(workbook.Path));
        var matches = await store.SearchAsync("", ".", recursive: true);
        matches.Select(match => match.FileName).ShouldBe([text.Path]);
        (await context.Documents.ReadWorkbookRangeAsync(workbook.Path, "Data", 1, 1, 1, 1)).Cells[0].Value.ShouldBe("source value");
    }

    private static void Rewrite(TestStorageScope scope, FileContextOptions options, string path)
    {
        var physical = Path.Combine(scope.Directory, options.RootPrefix, path);
        using var archive = ZipFile.Open(physical, ZipArchiveMode.Update);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        entry.Delete();
        using (var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open(), Encoding.UTF8))
        {
            writer.Write("""
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                <row r="1"><c r="A1" t="inlineStr"><is><t>EAN/UPC</t></is></c></row>
                <row r="2"><c r="A2" t="s"><v>0</v></c><c r="C2" t="inlineStr"><is><r><t>FT</t></r><r><t>0008</t></r></is></c><c r="D2"><f>SUM(B2:C2)</f><v>123456789012345.6</v></c></row>
                <row r="3"><c r="B3" t="e"><v>#N/A</v></c></row>
                </sheetData></worksheet>
                """);
        }
        using (var writer = new StreamWriter(archive.CreateEntry("xl/sharedStrings.xml").Open(), Encoding.UTF8))
        { writer.Write("""<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>000012345678</t></si></sst>"""); }
        AddElement(archive, "xl/_rels/workbook.xml.rels", """<Relationship xmlns="http://schemas.openxmlformats.org/package/2006/relationships" Id="strings" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/>""");
        AddElement(archive, "[Content_Types].xml", """<Override xmlns="http://schemas.openxmlformats.org/package/2006/content-types" PartName="/xl/sharedStrings.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml"/>""");
    }

    private static void AddElement(ZipArchive archive, string name, string xml)
    {
        var entry = archive.GetEntry(name)!;
        XDocument document;
        using (var stream = entry.Open()) { document = XDocument.Load(stream); }
        document.Root!.Add(XElement.Parse(xml));
        entry.Delete();
        using var target = archive.CreateEntry(name).Open();
        document.Save(target);
    }
}
