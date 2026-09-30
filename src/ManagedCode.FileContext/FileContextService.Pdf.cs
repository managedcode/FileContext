using ManagedCode.FileContext.Pdf;
using Microsoft.Extensions.AI;

namespace ManagedCode.FileContext;

public sealed partial class FileContextService
{
    private const string PdfExtension = ".pdf";

    public Task<FileContextPdfText> ReadPdfTextAsync(string path,
        int maxCharacters = FileContextDefaults.MaximumPdfTextCharacters,
        CancellationToken cancellationToken = default)
    {
        if (maxCharacters is < 1 or > FileContextDefaults.MaximumPdfTextCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }
        return FileContextOperation.RunAsync(_options.OperationTimeout, async token =>
        {
            using var permit = await _pdfProcessor.AcquireAsync(token).ConfigureAwait(false);
            var source = await OpenPdfAsync(path, token).ConfigureAwait(false);
            await using var sourceLifetime = source.ConfigureAwait(false);
            return FileContextPdfTextExtractor.Extract(source.Stream, maxCharacters, _options);
        }, cancellationToken);
    }

    public Task<DataContent> RenderPdfPageAsync(string path, int pageNumber,
        CancellationToken cancellationToken = default) =>
        FileContextOperation.RunAsync(_options.OperationTimeout, async token =>
        {
            using var permit = await _pdfProcessor.AcquireAsync(token).ConfigureAwait(false);
            var source = await OpenPdfAsync(path, token).ConfigureAwait(false);
            await using var sourceLifetime = source.ConfigureAwait(false);
            return FileContextImageContent.FromPngBytes(FileContextPdfImages.RenderPagePng(
                source.Stream, pageNumber, _options), _options);
        }, cancellationToken);

    public Task<int> CountPdfPageImagesAsync(string path, int pageNumber,
        CancellationToken cancellationToken = default) =>
        FileContextOperation.RunAsync(_options.OperationTimeout, async token =>
        {
            using var permit = await _pdfProcessor.AcquireAsync(token).ConfigureAwait(false);
            var source = await OpenPdfAsync(path, token).ConfigureAwait(false);
            await using var sourceLifetime = source.ConfigureAwait(false);
            return FileContextPdfImages.ExtractPageImagesPng(source.Stream, pageNumber, _options).Count;
        }, cancellationToken);

    public Task<DataContent> ExtractPdfImageAsync(string path, int pageNumber, int imageNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(imageNumber, 1);
        return FileContextOperation.RunAsync(_options.OperationTimeout, async token =>
        {
            using var permit = await _pdfProcessor.AcquireAsync(token).ConfigureAwait(false);
            var source = await OpenPdfAsync(path, token).ConfigureAwait(false);
            await using var sourceLifetime = source.ConfigureAwait(false);
            var images = FileContextPdfImages.ExtractPageImagesPng(source.Stream, pageNumber, _options);
            if (imageNumber > images.Count)
            {
                throw new InvalidOperationException("The embedded image number is outside this PDF page.");
            }
            return FileContextImageContent.FromPngBytes(images[imageNumber - 1].PngBytes, _options);
        }, cancellationToken);
    }

    private async Task<FileContextPdfSource> OpenPdfAsync(string path, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(StoragePathScope.Normalize(path)), PdfExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The file must have a .pdf extension.", nameof(path));
        }
        var metadata = await _fileStore.GetMetadataAsync(path, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"File '{path}' was not found.", path);
        if (metadata.Length > (ulong)_options.MaximumPdfReadBytes)
        {
            throw new IOException("The PDF exceeds the read limit.");
        }
        return await FileContextPdfSource.OpenAsync(
            await _fileStore.OpenReadAsync(path, cancellationToken).ConfigureAwait(false), _options, cancellationToken)
            .ConfigureAwait(false);
    }
}
