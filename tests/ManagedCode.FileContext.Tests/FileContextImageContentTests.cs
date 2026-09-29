using ManagedCode.FileContext.Pdf;
using Microsoft.Extensions.AI;

namespace ManagedCode.FileContext.Tests;

public sealed class FileContextImageContentTests
{
    [Fact]
    public void Png_bytes_base64_and_https_url_have_typed_model_content()
    {
        var png = FileContextPdfImages.RenderPagePng(FileContextPdfTestPdfs.ScannedPages(1), 1);
        var base64 = FileContextImageContent.ToBase64Png(png);
        var url = new Uri("https://media.example.com/page.png");

        FileContextImageContent.FromPngBytes(png).Data.ToArray().ShouldBe(png);
        FileContextImageContent.FromBase64Png(base64).Data.ToArray().ShouldBe(png);
        var reference = FileContextImageContent.FromPngUrl(url);
        reference.ShouldBeOfType<UriContent>();
        reference.Uri.ShouldBe(url);
    }

    [Fact]
    public void Image_content_rejects_invalid_or_oversized_sources()
    {
        var png = FileContextPdfImages.RenderPagePng(FileContextPdfTestPdfs.ScannedPages(1), 1);
        var options = new FileContextOptions { MaximumImageBytes = png.Length - 1 };

        Should.Throw<IOException>(() => FileContextImageContent.FromPngBytes(png, options));
        Should.Throw<InvalidDataException>(() => FileContextImageContent.FromPngBytes(new byte[] { 1, 2, 3 }));
        Should.Throw<ArgumentException>(() => FileContextImageContent.FromPngUrl(new Uri("http://media.example.com/page.png")));
    }
}
