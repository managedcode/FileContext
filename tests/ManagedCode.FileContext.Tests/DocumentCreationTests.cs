using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Extensions.AI;
using UglyToad.PdfPig;

namespace ManagedCode.FileContext.Tests;

public sealed class DocumentCreationTests
{
    [Fact]
    public async Task Csv_and_text_are_created_in_the_same_context_as_navigation_and_reads()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "conversation", EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var context = new FileContextService(store, options);
        var csv = await context.Documents.CreateCsvAsync("report.csv", new([["Назва", "A,B", "say \"hi\"", "first\r\nsecond", null]]));
        var text = await context.Documents.CreateTextAsync("report.csv", "separate file");
        csv.Path.ShouldNotBe(text.Path, StringComparer.Ordinal);
        csv.Path.ShouldStartWith("outputs/");
        (await store.ReadAsync(csv.Path))!.TrimStart('\uFEFF').ShouldBe("Назва,\"A,B\",\"say \"\"hi\"\"\",\"first\r\nsecond\",\r\n");
        (await context.GetInfoAsync(csv.Path))!.Length.ShouldBe((ulong)csv.Length);
        (await context.ListFilesAsync("outputs")).Select(file => file.Path).ShouldBe([csv.Path, text.Path], StringComparer.Ordinal, ignoreOrder: true);
        (await store.ListChildrenAsync("outputs")).Count.ShouldBe(2);
        (await context.ReadRangeAsync(text.Path)).Content.ShouldBe("separate file");
        var other = new FileContextService(new ManagedCodeStorageFileStore(storage.Storage, new() { RootPrefix = "other" }));
        (await other.ListFilesAsync()).ShouldBeEmpty();
        (await other.GetInfoAsync(csv.Path)).ShouldBeNull();
    }

    [Fact]
    public async Task Workbook_roundtrips_multiple_sheets_types_formulas_and_literal_text()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var context = new FileContextService(store, options);
        var created = await context.Documents.CreateWorkbookAsync("report.xlsx", new([
            new("Звіт", [[new(Text: "=SUM(A1:A3)"), new(Number: 12.5), new(Boolean: true), new(Formula: "=SUM(B1:B2)")]]),
            new("Details", [[new(Text: "line\nquoted \"value\""), new(Boolean: false), new()]])]));
        await using var stream = await store.OpenReadAsync(created.Path);
        using var document = SpreadsheetDocument.Open(stream, false);
        new OpenXmlValidator().Validate(document).Select(error => error.Description).ShouldBeEmpty();
        var workbook = document.WorkbookPart!;
        var sheets = workbook.Workbook!.Sheets!.Elements<Sheet>().ToArray();
        sheets.Select(sheet => sheet.Name!.Value).ShouldBe(["Звіт", "Details"]);
        var worksheet = (WorksheetPart)workbook.GetPartById(sheets[0].Id!);
        var cells = worksheet.Worksheet!.Descendants<Cell>().ToArray();
        cells[0].DataType!.Value.ShouldBe(CellValues.InlineString);
        cells[0].InnerText.ShouldBe("=SUM(A1:A3)");
        cells[1].CellValue!.Text.ShouldBe("12.5");
        cells[2].CellValue!.Text.ShouldBe("1");
        cells[3].CellFormula!.Text.ShouldBe("SUM(B1:B2)");
    }

    [Fact]
    public async Task Pdf_supports_unicode_wrapping_and_multiple_pages()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var paragraphs = Enumerable.Range(1, 110).Select(index => $"Рядок {index}: Український звіт — дані продажів.").ToArray();
        var created = await new FileContextService(store, options).Documents.CreatePdfAsync("report.pdf", new(paragraphs, "Звіт"));
        await using var stream = await store.OpenReadAsync(created.Path);
        using var document = PdfDocument.Open(stream);
        document.NumberOfPages.ShouldBeGreaterThan(1);
        var pages = document.GetPages().ToArray();
        var text = string.Join(" ", pages.Select(page => page.Text));
        text.ShouldContain("Український звіт");
        text.ShouldContain("Рядок 110");
        pages.SelectMany(page => page.Letters).All(letter => letter.BoundingBox.Left >= 49 &&
            letter.BoundingBox.Right <= 546 && letter.BoundingBox.Bottom >= 35 && letter.BoundingBox.Top < 842).ShouldBeTrue();
        var wrapped = await new FileContextService(store, options).Documents.CreatePdfAsync("wrapped.pdf",
            new([new string('W', 250), "line\r\nnext\tpart\rlast", ""]));
        wrapped.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Document_tools_are_opt_in_and_approval_wrapped_by_default()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions();
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        FileContextDocumentTools.Create(store, options).ShouldBeEmpty();
        await Should.ThrowAsync<InvalidOperationException>(() => new FileContextService(store, options).Documents.CreateTextAsync("x.txt", "x"));
        options.EnableWriteTools = true;
        var tools = FileContextDocumentTools.Create(store, options);
        tools.Count.ShouldBe(4);
        tools.ShouldAllBe(tool => tool is ApprovalRequiredAIFunction);
        options.RequireWriteToolApproval = false;
        var text = FileContextDocumentTools.Create(store, options).Single(tool => string.Equals(tool.Name, FileContextToolNames.CreateText, StringComparison.Ordinal));
        var output = (JsonElement)(await text.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal) { ["fileName"] = "notes.txt", ["text"] = "agent-created" }))!;
        var result = output.Deserialize<FileContextCreatedFile>(JsonSerializerOptions.Web)!;
        (await store.ReadAsync(result.Path)).ShouldBe("agent-created");
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("a//b.txt")]
    public async Task Creation_rejects_paths_outside_scope(string path)
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true, RootPrefix = "scope" };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        await Should.ThrowAsync<ArgumentException>(() => new FileContextService(store, options).Documents.CreateTextAsync(path, "forbidden"));
        (await new FileContextService(store, options).ListFilesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Binary_creation_enforces_output_budget_and_cancellation_before_upload()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true, MaximumGeneratedFileBytes = 4 };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes("12345"));
        await Should.ThrowAsync<IOException>(() => store.CreateFileAsync("large.bin", bytes, "application/octet-stream"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => new FileContextService(store, options).Documents.CreateTextAsync("x.txt", "x", cancelled.Token));
        (await new FileContextService(store, options).ListFilesAsync()).ShouldBeEmpty();
    }
}
