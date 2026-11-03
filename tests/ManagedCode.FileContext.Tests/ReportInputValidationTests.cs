using System.Text.Json;
using UglyToad.PdfPig;

namespace ManagedCode.FileContext.Tests;

public sealed class ReportInputValidationTests
{
    private static readonly JsonSerializerOptions StrictJson = new() { RespectRequiredConstructorParameters = true };

    [Fact]
    public async Task Page_break_can_omit_content()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var context = new FileContextService(store, options);
        var pageBreak = JsonSerializer.Deserialize<FileContextReportBlock>("""{"Type":"page-break"}""", StrictJson)!;
        var report = new FileContextReport("Report", "pages.pdf",
            [pageBreak, new("text", JsonSerializer.SerializeToElement(new { text = "Second page" }))], ["pdf"]);

        var result = await context.Documents.CreateReportAsync(report);

        await using var stream = await store.OpenReadAsync(result.Files.Single().Path);
        using var pdf = PdfDocument.Open(stream);
        pdf.NumberOfPages.ShouldBe(2);
        pdf.GetPage(2).Text.ShouldContain("Second page");
    }

    [Fact]
    public async Task Null_block_reports_its_index_without_persisting_any_files()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(storage.Storage, options), options);
        var report = new FileContextReport("Report", "bad.html", [null!], ["html"]);

        var error = await Should.ThrowAsync<ArgumentException>(() => context.Documents.CreateReportAsync(report));

        error.ParamName.ShouldBe("report");
        error.Message.ShouldContain("blocks[0]");
        (await context.ListFilesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Input_budget_counts_the_serialized_utf8_bytes_at_the_exact_boundary()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var report = new FileContextReport("Звіт 😀", "budget.html", [], ["html"]);
        var options = new FileContextOptions
        {
            EnableWriteTools = true,
            MaximumReportInputBytes = JsonSerializer.SerializeToUtf8Bytes(report).Length,
        };
        var context = new FileContextService(new ManagedCodeStorageFileStore(storage.Storage, options), options);

        var first = await context.Documents.CreateReportAsync(report);
        options.MaximumReportInputBytes--;
        var error = await Should.ThrowAsync<ArgumentException>(() => context.Documents.CreateReportAsync(report));

        error.Message.ShouldContain("Report input exceeds");
        (await context.ListFilesAsync()).Select(static file => file.Path).ShouldBe([first.Files.Single().Path]);
    }
}
