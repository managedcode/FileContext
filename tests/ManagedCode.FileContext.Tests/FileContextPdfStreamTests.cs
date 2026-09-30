using ManagedCode.FileContext.Pdf;

namespace ManagedCode.FileContext.Tests;

public sealed class FileContextPdfStreamTests
{
    [Fact]
    public async Task Non_seekable_sources_are_staged_on_disk_and_deleted_on_disposal()
    {
        var bytes = FileContextPdfTestPdfs.WithPages(["A complete family history with readable words."]);
        var input = new NonSeekableStream(bytes);
        var source = await FileContextPdfSource.OpenAsync(input, new FileContextOptions());
        var file = source.Stream.ShouldBeOfType<FileStream>();
        var path = file.Name;
        File.Exists(path).ShouldBeTrue();
        input.WasDisposed.ShouldBeTrue();
        FileContextPdfTextExtractor.Extract(source.Stream, 100).PageCount.ShouldBe(1);
        await source.DisposeAsync();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Seekable_sources_are_used_directly_and_kept_alive_through_page_rendering()
    {
        var input = new MemoryStream(FileContextPdfTestPdfs.ScannedPages(2));
        await using var source = await FileContextPdfSource.OpenAsync(input, new FileContextOptions());
        source.Stream.ShouldBeSameAs(input);
        using (var document = new FileContextPdfRenderDocument(source.Stream))
        {
            document.PageCount.ShouldBe(2);
            document.RenderPagePng(2).ShouldNotBeEmpty();
        }
        source.Stream.CanRead.ShouldBeTrue();
        FileContextPdfImages.ExtractPageImagesPng(source.Stream, 1).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Non_seekable_input_limit_failure_disposes_the_source()
    {
        var input = new NonSeekableStream(new byte[100]);
        await Should.ThrowAsync<IOException>(() => FileContextPdfSource.OpenAsync(input,
            new FileContextOptions { MaximumPdfReadBytes = 99 }));
        input.WasDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancellation_disposes_a_non_seekable_source()
    {
        var input = new NonSeekableStream(new byte[100]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => FileContextPdfSource.OpenAsync(input,
            new FileContextOptions(), cancellation.Token));
        input.WasDisposed.ShouldBeTrue();
    }

    [Fact]
    public void Direct_stream_apis_reject_non_seekable_input_instead_of_silently_buffering_it()
    {
        using var input = new NonSeekableStream(FileContextPdfTestPdfs.ScannedPages(1));
        Should.Throw<ArgumentException>(() => FileContextPdfImages.RenderPagePng(input, 1));
        Should.Throw<ArgumentException>(() => FileContextPdfTextExtractor.Extract(input, 100));
    }

    [Fact]
    public void Decoded_image_budget_is_enforced_even_when_the_output_scale_is_small()
    {
        var pdf = FileContextPdfTestPdfs.WithEmbeddedImages(2);
        var options = new FileContextOptions { MaximumDecodedPdfImagePixels = 1 };
        Should.Throw<IOException>(() => FileContextPdfImages.RenderPagePng(pdf, 1, options,
            options.MinimumPdfPageScale)).Message.ShouldContain("decoded image pixel limit");
        Should.Throw<IOException>(() => FileContextPdfImages.ExtractPageImagesPng(pdf, 1, options))
            .Message.ShouldContain("decoded image pixel limit");
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
