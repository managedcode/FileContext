using ManagedCode.FileContext.Pdf;
using ManagedCode.Storage.Core.Models;
using UglyToad.PdfPig;

namespace ManagedCode.FileContext.Tests;

public sealed class FileContextPdfTests
{
    private const string Interview = "Interview with grandmother about her childhood and marriage.";
    private const string Farm = "Then the family moved to the farm near Ephraim.";
    private const string PdfPath = "memories/interview.pdf";
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    [Fact]
    public void Text_reader_reports_page_count_and_scanned_pages()
    {
        var pdf = FileContextPdfTestPdfs.WithPages([Interview], [], [Farm]);
        var result = FileContextPdfTextExtractor.Extract(pdf, 1000);
        result.PageCount.ShouldBe(3);
        result.Text.ShouldBe($"{Interview}\n\n{Farm}");
        result.PagesWithoutText.ShouldBe([2]);
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Text_reader_preserves_separate_lines_within_a_page()
    {
        var pdf = FileContextPdfTestPdfs.WithPages([Interview, Farm]);
        var result = FileContextPdfTextExtractor.Extract(pdf, 1000);
        result.Text.ShouldBe($"{Interview}\n{Farm}");
        result.PagesWithoutText.ShouldBeEmpty();
    }

    [Fact]
    public void Scanned_page_can_be_rendered_and_its_embedded_picture_extracted()
    {
        var pdf = FileContextPdfTestPdfs.ScannedPages(2);
        var text = FileContextPdfTextExtractor.Extract(pdf, 1000);
        text.PageCount.ShouldBe(2);
        text.PagesWithoutText.ShouldBe([1, 2]);
        var page = FileContextPdfImages.RenderPagePng(pdf, 1);
        page.Take(PngSignature.Length).ShouldBe(PngSignature);
        var images = FileContextPdfImages.ExtractPageImagesPng(pdf, 1);
        images.ShouldHaveSingleItem().PngBytes.Take(PngSignature.Length).ShouldBe(PngSignature);
        using var rendered = PdfDocument.Open(pdf);
        rendered.NumberOfPages.ShouldBe(2);
    }

    [Fact]
    public async Task Scoped_file_tools_read_pdf_and_return_real_image_content()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "tenant" };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var pdf = FileContextPdfTestPdfs.ScannedPages(1);
        using var content = new MemoryStream(pdf);
        // Host-owned storage ingestion is separate from FileContext's opt-in write tools.
        var upload = await scope.Storage.UploadAsync(content, new UploadOptions { FileName = "tenant/" + PdfPath });
        upload.IsSuccess.ShouldBeTrue(upload.Problem?.Detail);
        var context = new FileContextService(store, options);
        var tools = new FileContextTools(context);
        (await tools.PdfTextAsync(PdfPath)).PagesWithoutText.ShouldBe([1]);
        (await tools.PdfImagesInfoAsync(PdfPath, 1)).ShouldBe(1);
        var page = await tools.PdfPageImageAsync(PdfPath, 1);
        page.MediaType.ShouldBe("image/png");
        page.Data.ToArray().Take(PngSignature.Length).ShouldBe(PngSignature);
        var embedded = await tools.PdfImageAsync(PdfPath, 1, 1);
        embedded.MediaType.ShouldBe("image/png");
        embedded.Data.ToArray().Take(PngSignature.Length).ShouldBe(PngSignature);
        await Should.ThrowAsync<ArgumentException>(() => context.ReadPdfTextAsync("../escape.pdf"));
        await Should.ThrowAsync<ArgumentException>(() => context.ReadPdfTextAsync("notes.txt"));
    }

    [Fact]
    public void Long_text_is_bounded_while_later_pages_are_still_inspected()
    {
        var longLine = string.Join(' ', Enumerable.Repeat("grandchild", 200));
        var pdf = FileContextPdfTestPdfs.WithPages([longLine], [], [Farm]);
        var result = FileContextPdfTextExtractor.Extract(pdf, 80);
        result.Text.Length.ShouldBe(80);
        result.TextCut.ShouldBeTrue();
        result.PageCount.ShouldBe(3);
        result.PagesWithoutText.ShouldBe([2]);
    }

    [Fact]
    public void Page_image_enforces_page_scale_and_input_limits()
    {
        var pdf = FileContextPdfTestPdfs.ScannedPages(1);
        Should.Throw<ArgumentOutOfRangeException>(() => FileContextPdfImages.RenderPagePng(pdf, 1, scale: 0.1));
        Should.Throw<IOException>(() => FileContextPdfImages.RenderPagePng(pdf, 1, scale: 3));
        Should.Throw<IOException>(() => FileContextPdfImages.RenderPagePng(new byte[FileContextPdfImages.MaximumPdfBytes + 1], 1));
        Should.Throw<ArgumentOutOfRangeException>(() => FileContextPdfImages.ExtractPageImagesPng(pdf, 2));
    }

    [Fact]
    public void Pdf_over_the_former_25_mib_limit_reaches_parsing()
    {
        var source = new byte[25 * 1024 * 1024 + 1];

        var exception = Record.Exception(() => FileContextPdfImages.RenderPagePng(source, 1));

        exception.ShouldNotBeNull();
        exception.Message.ShouldNotContain("The PDF exceeds the read limit.");
    }

    [Fact]
    public void Direct_page_rendering_obeys_the_configured_pdf_limit()
    {
        var pdf = FileContextPdfTestPdfs.ScannedPages(1);
        var options = new FileContextOptions { MaximumPdfReadBytes = pdf.Length - 1 };

        Should.Throw<IOException>(() => FileContextPdfImages.RenderPagePng(pdf, 1, options));
        options.MaximumPdfReadBytes = pdf.Length;
        FileContextPdfImages.RenderPagePng(pdf, 1, options).Take(PngSignature.Length)
            .ShouldBe(PngSignature);
    }

    [Fact]
    public void Page_and_image_budgets_come_from_file_context_options()
    {
        var pdf = FileContextPdfTestPdfs.ScannedPages(1);
        var options = new FileContextOptions { MaximumRenderedPagePixels = 1 };
        Should.Throw<IOException>(() => FileContextPdfImages.RenderPagePng(pdf, 1, options));

        options.MaximumRenderedPagePixels = FileContextDefaults.MaximumRenderedPagePixels;
        options.MaximumImageBytes = 1;
        Should.Throw<IOException>(() => FileContextPdfImages.RenderPagePng(pdf, 1, options));
        Should.Throw<IOException>(() => FileContextPdfImages.ExtractPageImagesPng(pdf, 1, options));
    }

    [Fact]
    public async Task Pdf_service_enforces_byte_and_image_index_limits()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = new FileContextOptions { RootPrefix = "tenant", MaximumPdfReadBytes = 10 };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var pdf = FileContextPdfTestPdfs.ScannedPages(1);
        var upload = await scope.Storage.UploadAsync(pdf, new UploadOptions { FileName = "tenant/" + PdfPath });
        upload.IsSuccess.ShouldBeTrue(upload.Problem?.Detail);
        var context = new FileContextService(store, options);
        await Should.ThrowAsync<IOException>(() => context.ReadPdfTextAsync(PdfPath));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => context.ReadPdfTextAsync(PdfPath, 0));
        options.MaximumPdfReadBytes = FileContextDefaults.MaximumPdfReadBytes;
        await Should.ThrowAsync<InvalidOperationException>(() => context.ExtractPdfImageAsync(PdfPath, 1, 2));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => context.ExtractPdfImageAsync(PdfPath, 1, 0));
        await Should.ThrowAsync<FileNotFoundException>(() => context.ReadPdfTextAsync("missing.pdf"));
    }

    [Fact]
    public void Text_layer_detection_accepts_script_without_spaces_and_rejects_page_numbers()
    {
        FileContextPdfTextExtractor.HasTextLayer("我们在一九三一年的舞会上认识了你的祖父他请我跳了两次舞").ShouldBeTrue();
        FileContextPdfTextExtractor.HasTextLayer("Page 3").ShouldBeFalse();
    }

    [Fact]
    public void Embedded_image_count_is_bounded()
    {
        var pdf = FileContextPdfTestPdfs.WithEmbeddedImages(FileContextPdfImages.MaximumImagesPerPage + 1);
        Should.Throw<IOException>(() => FileContextPdfImages.ExtractPageImagesPng(pdf, 1));
    }

    [Fact]
    public void Invalid_pdf_reports_an_error_and_invalid_page_is_rejected()
    {
        var invalid = "%PDF-1.7 not a real PDF"u8.ToArray();
        var result = FileContextPdfTextExtractor.Extract(invalid, 1000);
        result.PageCount.ShouldBeNull();
        result.Error.ShouldNotBeNullOrWhiteSpace();
        Should.Throw<ArgumentOutOfRangeException>(() => FileContextPdfImages.RenderPagePng(FileContextPdfTestPdfs.ScannedPages(1), 2));
    }
}
