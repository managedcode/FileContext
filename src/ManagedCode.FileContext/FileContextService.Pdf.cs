using ManagedCode.FileContext.Pdf;
using Microsoft.Extensions.AI;

namespace ManagedCode.FileContext;

public sealed partial class FileContextService
{
    private const string PdfExtension = ".pdf";
    private const int CopyBufferSize = 81920;

    public Task<FileContextPdfText> ReadPdfTextAsync(string path,
        int maxCharacters = FileContextDefaults.MaximumPdfTextCharacters,
        CancellationToken cancellationToken = default)
    {
        if (maxCharacters is < 1 or > FileContextDefaults.MaximumPdfTextCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }
        return FileContextOperation.RunAsync(_options.OperationTimeout, async token =>
            FileContextPdfTextExtractor.Extract(await ReadPdfBytesAsync(path, token).ConfigureAwait(false), maxCharacters), cancellationToken);
    }

    public Task<DataContent> RenderPdfPageAsync(string path, int pageNumber,
        CancellationToken cancellationToken = default) =>
        FileContextOperation.RunAsync(_options.OperationTimeout, async token =>
            FileContextImageContent.FromPngBytes(FileContextPdfImages.RenderPagePng(
                await ReadPdfBytesAsync(path, token).ConfigureAwait(false), pageNumber, _options), _options), cancellationToken);

    public Task<int> CountPdfPageImagesAsync(string path, int pageNumber,
        CancellationToken cancellationToken = default) =>
        FileContextOperation.RunAsync(_options.OperationTimeout, async token =>
            FileContextPdfImages.ExtractPageImagesPng(
                await ReadPdfBytesAsync(path, token).ConfigureAwait(false), pageNumber, _options).Count, cancellationToken);

    public Task<DataContent> ExtractPdfImageAsync(string path, int pageNumber, int imageNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(imageNumber, 1);
        return FileContextOperation.RunAsync(_options.OperationTimeout, async token =>
        {
            var images = FileContextPdfImages.ExtractPageImagesPng(
                await ReadPdfBytesAsync(path, token).ConfigureAwait(false), pageNumber, _options);
            if (imageNumber > images.Count)
            {
                throw new InvalidOperationException("The embedded image number is outside this PDF page.");
            }
            return FileContextImageContent.FromPngBytes(images[imageNumber - 1].PngBytes, _options);
        }, cancellationToken);
    }

    private async Task<byte[]> ReadPdfBytesAsync(string path, CancellationToken cancellationToken)
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
        var source = await _fileStore.OpenReadAsync(path, cancellationToken).ConfigureAwait(false);
        await using (source.ConfigureAwait(false))
        {
            using var output = new MemoryStream();
            var buffer = new byte[CopyBufferSize];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > _options.MaximumPdfReadBytes)
                {
                    throw new IOException("The PDF exceeds the read limit.");
                }
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
            return output.ToArray();
        }
    }
}
