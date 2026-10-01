using ManagedCode.FileContext.Pdf;

namespace ManagedCode.FileContext.Tests;

public sealed class FileContextPdfSourceStagingTests
{
    private const int BufferBytes = 4096;
    private const long LargeSourceBytes = (8L * 1024 * 1024) + 1;

    [Fact]
    public async Task Seekable_cloud_source_is_staged_before_text_and_page_parsing()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = Options(scope);
        var path = Path.Combine(scope.Directory, "scan.pdf");
        await File.WriteAllBytesAsync(path, FileContextPdfTestPdfs.ScannedPages(43));
        var input = new AsyncOnlySeekablePdfStream(File.OpenRead(path));
        var source = await FileContextPdfSource.OpenAsync(input, options);
        var file = source.Stream.ShouldBeOfType<FileStream>();
        try
        {
            input.WasDisposed.ShouldBeTrue();
            input.MaximumReadRequestBytes.ShouldBeLessThanOrEqualTo(BufferBytes);
            file.Name.ShouldStartWith(options.PdfTemporaryDirectory!);
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(file.Name).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            FileContextPdfTextExtractor.Extract(file, 100).PageCount.ShouldBe(43);
            FileContextPdfImages.RenderPagePng(file, 16).ShouldNotBeEmpty();
            FileContextPdfImages.ExtractPageImagesPng(file, 9).ShouldHaveSingleItem();
        }
        finally { await source.DisposeAsync(); }
        Directory.GetFiles(options.PdfTemporaryDirectory!).ShouldBeEmpty();
    }

    [Fact]
    public async Task Large_cloud_source_uses_bounded_reads_and_preserves_random_access()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = Options(scope);
        var path = Path.Combine(scope.Directory, "large.pdf");
        await using (var output = File.Create(path))
        {
            output.SetLength(LargeSourceBytes);
            await output.WriteAsync("%PDF-"u8.ToArray());
            output.Position = LargeSourceBytes - 1;
            output.WriteByte(byte.MaxValue);
        }
        var input = new AsyncOnlySeekablePdfStream(File.OpenRead(path));
        await using (var source = await FileContextPdfSource.OpenAsync(input, options))
        {
            source.Stream.Length.ShouldBe(LargeSourceBytes);
            source.Stream.Seek(-1, SeekOrigin.End);
            source.Stream.ReadByte().ShouldBe(byte.MaxValue);
            source.Stream.Position = 0;
            source.Stream.ReadByte().ShouldBe('%');
            input.MaximumReadRequestBytes.ShouldBe(BufferBytes);
            input.WasDisposed.ShouldBeTrue();
        }
        Directory.GetFiles(options.PdfTemporaryDirectory!).ShouldBeEmpty();
    }

    [Fact]
    public async Task Temporary_file_mode_stages_local_sources_in_the_configured_directory()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = Options(scope);
        options.PdfSourceStagingMode = FileContextPdfSourceStagingMode.TemporaryFile;
        var input = new MemoryStream(FileContextPdfTestPdfs.WithPages(["Readable family history with enough words."]));
        await using (var source = await FileContextPdfSource.OpenAsync(input, options))
        {
            source.Stream.ShouldBeOfType<FileStream>();
            input.CanRead.ShouldBeFalse();
            FileContextPdfTextExtractor.Extract(source.Stream, 100).Text.ShouldContain("family history");
        }
        Directory.GetFiles(options.PdfTemporaryDirectory!).ShouldBeEmpty();
    }

    [Fact]
    public async Task Local_file_is_reused_without_creating_a_second_source()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = Options(scope);
        var path = Path.Combine(scope.Directory, "local.pdf");
        await File.WriteAllBytesAsync(path, FileContextPdfTestPdfs.ScannedPages(1));
        var input = File.OpenRead(path);
        await using (var source = await FileContextPdfSource.OpenAsync(input, options))
        {
            source.Stream.ShouldBeSameAs(input);
            FileContextPdfImages.RenderPagePng(source.Stream, 1).ShouldNotBeEmpty();
            Directory.GetFiles(options.PdfTemporaryDirectory!).ShouldBeEmpty();
        }
        input.CanRead.ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Limit_failure_disposes_source_and_removes_staged_file(bool seekable)
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = Options(scope);
        options.MaximumPdfReadBytes = BufferBytes;
        var input = await InputAsync(scope, BufferBytes + 1);
        input.HideSeekability = !seekable;
        await Should.ThrowAsync<IOException>(() => FileContextPdfSource.OpenAsync(input, options));
        input.WasDisposed.ShouldBeTrue();
        Directory.GetFiles(options.PdfTemporaryDirectory!).ShouldBeEmpty();
    }

    [Fact]
    public async Task Mid_copy_cancellation_disposes_source_and_removes_staged_file()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = Options(scope);
        using var cancellation = new CancellationTokenSource();
        var input = await InputAsync(scope, BufferBytes + 1);
        input.CancelAfterRead = cancellation;
        await Should.ThrowAsync<OperationCanceledException>(() =>
            FileContextPdfSource.OpenAsync(input, options, cancellation.Token));
        input.WasDisposed.ShouldBeTrue();
        Directory.GetFiles(options.PdfTemporaryDirectory!).ShouldBeEmpty();
    }

    [Fact]
    public async Task Read_failure_disposes_source_and_removes_staged_file()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var options = Options(scope);
        var input = await InputAsync(scope, BufferBytes);
        input.FailRead = true;
        await Should.ThrowAsync<IOException>(() => FileContextPdfSource.OpenAsync(input, options));
        input.WasDisposed.ShouldBeTrue();
        Directory.GetFiles(options.PdfTemporaryDirectory!).ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_staging_options_are_rejected_before_source_reads()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        var configurations = new FileContextOptions[]
        {
            new() { PdfSourceBufferBytes = 0 },
            new() { PdfSourceStagingMode = (FileContextPdfSourceStagingMode)int.MaxValue },
            new() { PdfTemporaryDirectory = " " }
        };
        foreach (var options in configurations)
        {
            var input = await InputAsync(scope, 1);
            await Should.ThrowAsync<InvalidOperationException>(() => FileContextPdfSource.OpenAsync(input, options));
            input.WasDisposed.ShouldBeTrue();
            input.MaximumReadRequestBytes.ShouldBe(0);
        }
    }

    private static async Task<AsyncOnlySeekablePdfStream> InputAsync(TestStorageScope scope, int bytes)
    {
        var path = Path.Combine(scope.Directory, Guid.NewGuid().ToString("N"));
        await File.WriteAllBytesAsync(path, new byte[bytes]).ConfigureAwait(false);
        return new AsyncOnlySeekablePdfStream(File.OpenRead(path));
    }

    private static FileContextOptions Options(TestStorageScope scope)
    {
        var directory = Path.Combine(scope.Directory, "staged");
        Directory.CreateDirectory(directory);
        return new FileContextOptions { PdfTemporaryDirectory = directory, PdfSourceBufferBytes = BufferBytes };
    }
}
