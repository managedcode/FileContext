using ManagedCode.FileContext.Pdf;
using ManagedCode.Storage.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.FileContext.Tests;

public sealed class FileContextPdfProcessingTests
{
    [Fact]
    public async Task Native_pdf_tool_waits_on_the_shared_processor_before_reading()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var pdf = FileContextPdfTestPdfs.ScannedPages(1);
        var upload = await scope.Storage.UploadAsync(pdf, new UploadOptions { FileName = "scan.pdf" });
        upload.IsSuccess.ShouldBeTrue();
        var options = new FileContextOptions();
        using var processor = new FileContextPdfProcessor(options);
        using var permit = await processor.AcquireAsync();
        var context = new FileContextService(new ManagedCodeStorageFileStore(scope.Storage, options), options, processor);
        var render = new FileContextTools(context).PdfPageImageAsync("scan.pdf", 1);
        render.IsCompleted.ShouldBeFalse();
        permit.Dispose();
        (await render.WaitAsync(TimeSpan.FromSeconds(5))).Data.IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public async Task Shared_processor_waits_before_source_work_and_releases_once()
    {
        using var processor = new FileContextPdfProcessor(new FileContextOptions());
        var first = await processor.AcquireAsync();
        var waiting = processor.AcquireAsync();
        waiting.IsCompleted.ShouldBeFalse();
        first.Dispose();
        first.Dispose();
        using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        var third = processor.AcquireAsync();
        third.IsCompleted.ShouldBeFalse();
        second.Dispose();
        using var last = await third.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cancelled_wait_does_not_leak_a_permit()
    {
        using var processor = new FileContextPdfProcessor(new FileContextOptions());
        using var first = await processor.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = processor.AcquireAsync(cancellation.Token);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
        first.Dispose();
        using var next = await processor.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Host_options_share_one_configured_processor()
    {
        var services = new ServiceCollection();
        services.AddManagedCodeFileContext(options => options.MaximumConcurrentPdfOperations = 2);
        using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<FileContextPdfProcessor>();
        processor.ShouldBeSameAs(provider.GetRequiredService<FileContextPdfProcessor>());
        using var first = await processor.AcquireAsync();
        using var second = await processor.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = processor.AcquireAsync(cancellation.Token);
        waiting.IsCompleted.ShouldBeFalse();
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => waiting);
        Should.Throw<InvalidOperationException>(() => new FileContextPdfProcessor(
            new FileContextOptions { MaximumConcurrentPdfOperations = 0 }));
    }

    [Fact]
    public void One_parsed_document_exposes_count_and_renders_multiple_pages_with_limits()
    {
        var pdf = FileContextPdfTestPdfs.ScannedPages(3);
        using var document = new FileContextPdfRenderDocument(pdf);
        document.PageCount.ShouldBe(3);
        document.RenderPagePng(3).ShouldBe(FileContextPdfImages.RenderPagePng(pdf, 3));
        document.RenderPagePng(1).ShouldNotBeEmpty();
        Should.Throw<ArgumentOutOfRangeException>(() => document.RenderPagePng(4));
        Should.Throw<ArgumentOutOfRangeException>(() => document.RenderPagePng(1, double.NaN));
        Should.Throw<IOException>(() => new FileContextPdfRenderDocument(pdf,
            new FileContextOptions { MaximumPdfReadBytes = pdf.Length - 1 }));
        using var limited = new FileContextPdfRenderDocument(pdf,
            new FileContextOptions { MaximumRenderedPagePixels = 1 });
        Should.Throw<IOException>(() => limited.RenderPagePng(1));
    }
}
