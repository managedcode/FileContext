using System.Text.Json;
using ManagedCode.Storage.Core.Models;
using UglyToad.PdfPig;

namespace ManagedCode.FileContext.Tests;

public sealed class DefaultCompletenessTests
{
    [Fact]
    public async Task Text_reads_and_search_include_every_line_by_default()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var context = new FileContextService(store);
        const int lineCount = 1_101;
        var text = string.Join(Environment.NewLine, Enumerable.Repeat("needle " + new string('a', 1_000), lineCount));
        await store.WriteAsync("large.txt", text);

        (await store.ReadAsync("large.txt")).ShouldBe(text);
        var range = await context.ReadRangeAsync("large.txt");
        range.Content.ShouldBe(text);
        range.TotalLines.ShouldBe(lineCount);
        range.HasMore.ShouldBeFalse();
        (await store.SearchAsync("", "needle")).Single().MatchingLines.Count.ShouldBe(lineCount);
    }

    [Fact]
    public async Task Csv_larger_than_the_former_default_is_uploaded_in_full()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        const int fieldLength = 8_192;
        const int rowCount = 8_192;
        IReadOnlyList<string?> row = [new string('x', fieldLength)];
        var rows = Enumerable.Repeat(row, rowCount).Append(["last"]).ToArray();

        var created = await context.Documents.CreateCsvAsync("large.csv", new(rows));

        created.Length.ShouldBe(3L + (fieldLength + 2L) * rowCount + 6L);
        await using var source = await store.OpenReadAsync(created.Path);
        source.Seek(-6, SeekOrigin.End);
        using var reader = new StreamReader(source, leaveOpen: true);
        (await reader.ReadToEndAsync()).ShouldBe("last\r\n");
    }

    [Fact]
    public async Task Docx_reads_beyond_the_previous_paragraph_and_character_ceilings()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var store = new ManagedCodeStorageFileStore(scope.Storage);
        var context = new FileContextService(store);
        var paragraphs = Enumerable.Repeat(new string('a', 450), 55).Append("last").ToArray();
        var upload = await scope.Storage.UploadAsync(FileContextDocxTestFiles.WithParagraphs(paragraphs),
            new UploadOptions { FileName = "long.docx" });
        upload.IsSuccess.ShouldBeTrue();

        var text = await context.Documents.ReadDocxTextAsync("long.docx");

        text.Paragraphs.Select(static paragraph => paragraph.Text).ShouldBe(paragraphs);
        text.NextParagraph.ShouldBeNull();
    }

    [Fact]
    public async Task Report_can_render_more_than_sixty_pages_and_six_cards()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        var kpis = new FileContextReportBlock("kpi-row", JsonSerializer.SerializeToElement(new
        {
            items = Enumerable.Range(1, 9).Select(static index => new { label = $"Card {index}", value = "1" }),
        }));
        var blocks = Enumerable.Repeat(new FileContextReportBlock("page-break"), 60).Append(kpis).ToArray();

        var report = await context.Documents.CreateReportAsync(new("Report", "large.pdf", blocks, ["pdf", "html"]));

        await using var source = await store.OpenReadAsync(report.Files[0].Path);
        using var pdf = PdfDocument.Open(source);
        pdf.NumberOfPages.ShouldBe(61);
        pdf.GetPage(61).Text.ShouldContain("Card 9");
        (await store.ReadAsync(report.Files[1].Path))!.ShouldContain("Card 9");
    }
}
