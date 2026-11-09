using SkiaSharp;
using UglyToad.PdfPig;

namespace ManagedCode.FileContext.Tests;

public sealed class ReportLogoTests
{
    [Fact]
    public async Task Html_report_rejects_logo_markup_before_upload()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        var report = new FileContextReport("Brand", "logo", [], ["html"],
            Theme: new FileContextReportTheme(LogoBase64: "broken\"><p>injected</p>"));

        var error = await Should.ThrowAsync<ArgumentException>(() => context.Documents.CreateReportAsync(report));

        error.Message.ShouldContain("logo");
        (await store.ListChildrenAsync("outputs")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Jpeg_logo_uses_its_media_type_and_appears_on_each_pdf_page()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { EnableWriteTools = true };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var context = new FileContextService(store, options);
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.Blue);
        using var jpeg = bitmap.Encode(SKEncodedImageFormat.Jpeg, 90);
        var report = new FileContextReport("Brand", "logo", [new("page-break")], ["html", "pdf"],
            Theme: new FileContextReportTheme(LogoBase64: Convert.ToBase64String(jpeg.ToArray())));

        var output = await context.Documents.CreateReportAsync(report);
        var html = output.Files.Single(file => file.Path.EndsWith(".html", StringComparison.Ordinal));
        (await store.ReadAsync(html.Path)).ShouldNotBeNull().ShouldContain("data:image/jpeg;base64,");
        var pdf = output.Files.Single(file => file.Path.EndsWith(".pdf", StringComparison.Ordinal));
        await using var stream = await store.OpenReadAsync(pdf.Path);
        using var document = PdfDocument.Open(stream);
        document.NumberOfPages.ShouldBe(2);
        foreach (var page in document.GetPages())
        {
            var image = page.GetImages().ShouldHaveSingleItem();
            (image.WidthInSamples, image.HeightInSamples).ShouldBe((4, 4));
        }
    }
}
