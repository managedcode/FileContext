// Small immutable arrays are readable in these report fixtures.
#pragma warning disable CA1861
using System.Text.Json;
using UglyToad.PdfPig;

namespace ManagedCode.FileContext.Tests;

public sealed class ReportCreationTests
{
    [Fact]
    public async Task Creates_branded_pdf_html_and_chart_png_in_same_scoped_store()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "reports", EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var report = new FileContextReport("Marketing pulse", "pulse.pdf",
        [
            Block("heading", new { title = "September performance", subtitle = "NYWD marketing" }),
            Block("kpi-row", new { items = new[] { new { label = "Revenue", value = "$125k", change = "+12%" }, new { label = "Leads", value = "42", change = "+5%" } } }),
            Block("chart", new { title = "Channel sales", chartType = "bar", labels = new[] { "Search", "Email" }, series = new[] { new { name = "Sales", values = new[] { 12, 8 } } } }),
            Block("table", new { columns = new[] { new { key = "name", label = "Channel" }, new { key = "sales", label = "Sales" } }, rows = new[] { new { name = "Search", sales = "12" } }, totals = new { name = "Total", sales = "12" } }),
            Block("callout", new { text = "Growth is above plan." })
        ], ["pdf", "html", "png"]);
        var result = await new FileContextService(store, options).Documents.CreateReportAsync(report);
        result.Files.Count.ShouldBe(3);
        var pdf = result.Files.Single(file => file.Path.EndsWith(".pdf", StringComparison.Ordinal));
        await using var pdfStream = await store.OpenReadAsync(pdf.Path);
        using var document = PdfDocument.Open(pdfStream);
        document.NumberOfPages.ShouldBe(1);
        document.GetPage(1).Text.ShouldContain("September performance");
        document.GetPage(1).Text.ShouldContain("Search");
        var html = result.Files.Single(file => file.Path.EndsWith(".html", StringComparison.Ordinal));
        var text = await store.ReadAsync(html.Path);
        text!.ShouldContain("Content-Security-Policy");
        text!.ShouldContain("Growth is above plan.");
        text!.ShouldContain("data:image/png;base64,");
        var png = result.Files.Single(file => file.Path.EndsWith(".png", StringComparison.Ordinal));
        await using var pngStream = await store.OpenReadAsync(png.Path);
        var signature = new byte[8];
        (await pngStream.ReadAsync(signature)).ShouldBe(8);
        signature.ShouldBe(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    }

    [Fact]
    public async Task Repeats_table_header_and_counts_pages_without_splitting_rows()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var report = new FileContextReport("Inventory", "inventory", [Block("table", new
        {
            columns = new[] { new { key = "sku", label = "SKU" }, new { key = "stock", label = "Stock" } },
            rows = Enumerable.Range(1, 60).Select(index => new { sku = $"SKU-{index}", stock = index }).ToArray()
        })], ["pdf"]);
        var result = await new FileContextService(store, options).Documents.CreateReportAsync(report);
        await using var stream = await store.OpenReadAsync(result.Files[0].Path);
        using var document = PdfDocument.Open(stream);
        document.NumberOfPages.ShouldBeGreaterThan(1);
        document.GetPage(2).Text.ShouldContain("SKU");
        document.GetPages().Last().Text.ShouldContain("SKU-60");
    }

    [Fact]
    public async Task Invalid_block_fails_before_creating_a_file_with_block_index()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var report = new FileContextReport("Bad", "bad", [Block("chart", new { chartType = "line", labels = new[] { "A" } })], ["pdf"]);
        var error = await Should.ThrowAsync<ArgumentException>(() => new FileContextService(store, options).Documents.CreateReportAsync(report));
        error.Message.ShouldContain("blocks[0]");
        (await store.ListChildrenAsync("outputs")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Disabled_report_writes_are_rejected_before_rendering_or_validating_content()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions();
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var report = new FileContextReport("Report", "test", [], ["unsupported"]);

        await Should.ThrowAsync<InvalidOperationException>(() => context.Documents.CreateReportAsync(report));
    }

    [Fact]
    public async Task Report_output_budget_is_checked_before_uploading_any_artifact()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options);
        var report = new FileContextReport("Report", "test", [Block("text", new { text = "Report content" })], ["html"]);
        var first = (await context.Documents.CreateReportAsync(report)).Files.Single();
        options.MaximumGeneratedFileBytes = first.Length;

        await Should.ThrowAsync<IOException>(() => context.Documents.CreateReportAsync(report with { Outputs = ["html", "pdf"] }));

        (await context.ListFilesAsync()).Select(static file => file.Path).ShouldBe([first.Path]);
    }


    [Theory]
    [InlineData("line")]
    [InlineData("bar")]
    [InlineData("stacked-bar")]
    [InlineData("pie")]
    [InlineData("donut")]
    public async Task All_chart_types_render_as_png(string chartType)
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var report = new FileContextReport("Charts", "charts", [Block("chart", new
        {
            title = "Channels", chartType, labels = new[] { "Search", "Email" },
            series = new[] { new { name = "Visits", values = new[] { 2, 4 } } },
            xAxis = "Channel", yAxis = "Visits"
        })], ["pdf", "png"]);
        var output = await new FileContextService(store, options).Documents.CreateReportAsync(report);
        var png = output.Files.Single(file => string.Equals(file.MediaType, "image/png", StringComparison.Ordinal));
        await using var pngStream = await store.OpenReadAsync(png.Path);
        var signature = new byte[8];
        (await pngStream.ReadAsync(signature)).ShouldBe(8);
        signature.ShouldBe(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        await using var pdfStream = await store.OpenReadAsync(output.Files.Single(file => string.Equals(file.MediaType, "application/pdf", StringComparison.Ordinal)).Path);
        using var pdf = PdfDocument.Open(pdfStream);
        pdf.GetPage(1).Text.ShouldContain("Channels");
    }

    [Fact]
    public async Task Text_formatting_and_formatted_highlighted_table_survive_pdf_and_html()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var report = new FileContextReport("Summary", "summary", [
            Block("text", new { text = "**Important**\n- Review [source](https://nywd.com/report)" }),
            Block("table", new {
                columns = new[] { new { key = "channel", label = "Channel", format = "" }, new { key = "value", label = "Value", format = "N1" } },
                rows = new[] { new { channel = "Search", value = 12.5 } },
                highlightCells = new object[] { new { row = 0, column = "value" }, new { row = 0, column = "value" },
                    new { row = 0, column = "CHANNEL" }, new { row = "0", column = "channel" } }
            })
        ], ["pdf", "html"]);
        var outputs = await new FileContextService(store, options).Documents.CreateReportAsync(report);
        await using var pdfStream = await store.OpenReadAsync(outputs.Files.Single(file => string.Equals(file.MediaType, "application/pdf", StringComparison.Ordinal)).Path);
        using var pdf = PdfDocument.Open(pdfStream);
        pdf.GetPage(1).Text.ShouldContain("Important");
        pdf.GetPage(1).Text.ShouldContain("12.5");
        var html = await store.ReadAsync(outputs.Files.Single(file => file.MediaType.StartsWith("text/html", StringComparison.Ordinal)).Path);
        html!.ShouldContain("<strong>Important</strong>");
        html!.ShouldContain("<li>");
        html!.ShouldContain("<a href=\"https://nywd.com/report\" rel=\"noreferrer\">source</a>");
        html!.ShouldContain("<td>Search</td><td class=\"highlight\">12.5</td>");
    }

    [Fact]
    public async Task Html_preserves_long_unmatched_inline_markup()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var text = new string('[', 1_000_000) + " **Tail**";
        var report = new FileContextReport("Inline markup", "inline", [Block("text", new { text })], ["html"]);

        var output = await new FileContextService(store, options).Documents.CreateReportAsync(report);
        var html = await store.ReadAsync(output.Files.Single().Path);

        html!.ShouldContain(new string('[', 1_000_000) + " <strong>Tail</strong>");
    }

    [Fact]
    public async Task Explicit_page_break_starts_a_new_branded_page()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var report = new FileContextReport("Quarterly report", "quarterly.PDF", [
            Block("heading", new { title = "First section" }),
            Block("page-break", new { }),
            Block("heading", new { title = "Second section" })
        ], ["pdf"]);
        var result = await new FileContextService(store, options).Documents.CreateReportAsync(report);
        result.Files.Single().Path.ShouldEndWith("quarterly.pdf");
        await using var stream = await store.OpenReadAsync(result.Files.Single().Path);
        using var pdf = PdfDocument.Open(stream);
        pdf.NumberOfPages.ShouldBe(2);
        pdf.GetPage(1).Text.ShouldContain("First section");
        pdf.GetPage(2).Text.ShouldContain("Second section");
        pdf.GetPage(2).Text.ShouldContain("Quarterly report");
    }

    [Fact]
    public void Pdf_wrap_preserves_exact_fit_overflow_empty_lines_and_unicode()
    {
        using var paint = ReportCanvas.Paint(SkiaSharp.SKColors.Black, 10);
        var width = ReportCanvas.MeasureText("WW", paint);

        var lines = ReportCanvas.Wrap("WW\r\nWWW\n\nЇ😀", paint, width);

        lines.ShouldBe(["WW", "WW", "W", "", "Ї😀"]);
        ReportCanvas.Wrap("😀😀", paint, 0).ShouldBe(["😀", "😀"]);
    }

    [Fact]
    public async Task Long_pdf_heading_flows_across_pages_and_honors_an_explicit_page_budget()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var context = new FileContextService(store, options);
        var title = string.Join('\n', Enumerable.Range(1, 30).Select(index => $"Section {index}"));
        var report = new FileContextReport("Overview", "overview", [
            Block("heading", new { title, subtitle = "Final subtitle" }),
            Block("text", new { text = "Body follows" })
        ], ["pdf"]);

        var output = (await context.Documents.CreateReportAsync(report)).Files.Single();
        await using var stream = await store.OpenReadAsync(output.Path);
        using var document = PdfDocument.Open(stream);
        document.NumberOfPages.ShouldBe(2);
        document.GetPage(1).Text.ShouldContain("Section 1");
        document.GetPage(2).Text.ShouldContain("Section 30");
        document.GetPage(2).Text.ShouldContain("Final subtitle");
        document.GetPage(2).Text.ShouldContain("Body follows");

        options.MaximumReportPages = 1;
        var error = await Should.ThrowAsync<ArgumentException>(() => context.Documents.CreateReportAsync(report));
        error.Message.ShouldContain("exceeds 1 pages");
        (await context.ListFilesAsync()).Select(static file => file.Path).ShouldBe([output.Path]);
    }

    [Fact]
    public async Task Unsupported_image_fails_before_any_requested_output_is_written()
    {
        await using var storage = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(storage.Storage, options);
        var report = new FileContextReport("Images", "images", [
            Block("image", new { base64 = Convert.ToBase64String(new byte[16]), caption = "Product" })
        ], ["html", "pdf"]);
        var error = await Should.ThrowAsync<ArgumentException>(() => new FileContextService(store, options).Documents.CreateReportAsync(report));
        error.Message.ShouldContain("blocks[0]");
        (await store.ListChildrenAsync("outputs")).ShouldBeEmpty();
    }

    private static FileContextReportBlock Block<T>(string type, T content) => new(type, JsonSerializer.SerializeToElement(content));
}
