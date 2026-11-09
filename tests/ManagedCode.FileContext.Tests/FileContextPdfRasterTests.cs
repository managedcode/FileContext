using ManagedCode.FileContext.Pdf;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Rendering.Skia;
using UglyToad.PdfPig.Writer;

namespace ManagedCode.FileContext.Tests;

public sealed class FileContextPdfRasterTests
{
    private const string Interview = "Interview with grandmother about her childhood and marriage.";

    [Fact]
    public void Page_with_a_standard_font_renders_visible_text()
    {
        var pdf = FileContextPdfTestPdfs.WithPages([Interview]);

        using var image = SKBitmap.Decode(FileContextPdfImages.RenderPagePng(pdf, 1, scale: 1));

        image.ShouldNotBeNull();
        image.Pixels.ShouldContain(pixel => pixel.Alpha > 0 && pixel.Red < 128);
    }

    [Fact]
    public void Bundled_font_factory_renders_visible_text_on_unix()
    {
        // Exercise the fallback on Unix even when the machine has a usable system font.
        if (OperatingSystem.IsWindows()) { return; }

        var pdf = FileContextPdfTestPdfs.WithPages([Interview]);
        using var document = PdfDocument.Open(pdf, SkiaRenderingParsingOptions.Instance);
        document.AddPageFactory<PdfPageSize, PageSizeFactory>();
        document.AddPageFactory<SKPicture, FileContextPdfPageFactory>();

        using var image = document.GetPageAsSKBitmap(1);
        image.Pixels.ShouldContain(pixel => pixel.Alpha > 0 && pixel.Red < 128);
    }

    [Fact]
    public void Pixel_budget_accounts_for_rounded_raster_dimensions()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(100, 100);
        var pdf = builder.Build();
        var options = new FileContextOptions { MaximumRenderedPagePixels = 3 };

        var error = Should.Throw<IOException>(() => FileContextPdfImages.RenderPagePng(pdf, 1, options, scale: 0.015));
        error.Message.ShouldContain("pixel limit");

        options.MaximumRenderedPagePixels = 4;
        using var image = SKBitmap.Decode(FileContextPdfImages.RenderPagePng(pdf, 1, options, scale: 0.015));
        (image.Width, image.Height).ShouldBe((2, 2));
    }
}
