using DocumentFormat.OpenXml.Packaging;

namespace ManagedCode.FileContext;

public sealed partial class FileContextDocumentService
{
    public Task<FileContextWorkbookInfo> GetWorkbookInfoAsync(string path, CancellationToken cancellationToken = default) =>
        ReadWorkbookAsync(path, (document, token) => FileContextWorkbookReader.Inspect(document, path, token), cancellationToken);

    public Task<FileContextWorkbookRange> ReadWorkbookRangeAsync(string path, string sheet, int startRow, int rowCount,
        int startColumn, int columnCount, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sheet);
        FileContextWorkbookReader.ValidateRange(startRow, rowCount, startColumn, columnCount);
        return ReadWorkbookAsync(path, (document, token) => FileContextWorkbookReader.Read(document, path, sheet,
            startRow, rowCount, startColumn, columnCount, options.MaximumRangeReadBytes, token), cancellationToken);
    }

    private Task<T> ReadWorkbookAsync<T>(string path, Func<SpreadsheetDocument, CancellationToken, T> read, CancellationToken cancellationToken)
    {
        RequireExtension(path, ".xlsx");
        return ReadSourceAsync(path, (buffer, token) =>
        {
            using var document = SpreadsheetDocument.Open(buffer, false,
                new OpenSettings { MaxCharactersInPart = options.MaximumOpenXmlPartCharacters });
            return read(document, token);
        }, options.MaximumFullReadBytes, cancellationToken);
    }

    private Task<T> ReadSourceAsync<T>(string path, Func<Stream, CancellationToken, T> read,
        long maximumBytes, CancellationToken cancellationToken) =>
        FileContextOperation.RunAsync(options.OperationTimeout, async token =>
        {
            var source = await OpenScopedSourceAsync(path, maximumBytes, token).ConfigureAwait(false);
            await using var lifetime = source.ConfigureAwait(false);
            if (maximumBytes == long.MaxValue && source is FileStream or MemoryStream && source.CanSeek && source.Position == 0)
            {
                token.ThrowIfCancellationRequested();
                return read(source, token);
            }

            using var buffer = new MemoryStream();
            await CopySourceAsync(source, buffer, maximumBytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return read(buffer, token);
        }, cancellationToken);

    private async Task CopyScopedSourceAsync(string path, MemoryStream buffer, long maximumBytes, CancellationToken token)
    {
        var source = await OpenScopedSourceAsync(path, maximumBytes, token).ConfigureAwait(false);
        await using var lifetime = source.ConfigureAwait(false);
        await CopySourceAsync(source, buffer, maximumBytes, token).ConfigureAwait(false);
    }

    private async Task<Stream> OpenScopedSourceAsync(string path, long maximumBytes, CancellationToken token)
    {
        var metadata = await store.GetMetadataAsync(path, token).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The document was not found in this file context.", path);
        if (metadata.Length > (ulong)maximumBytes)
        { throw new IOException("Document exceeds the configured source read budget."); }
        return await store.OpenReadInOperationAsync(path, token).ConfigureAwait(false);
    }

    private static async Task CopySourceAsync(Stream source, MemoryStream buffer, long maximumBytes, CancellationToken token)
    {
        using var bounded = new StorageReadBudgetStream(source, maximumBytes);
        await bounded.CopyToAsync(buffer, StorageReadBudgetStream.DefaultCopyBufferSize, token).ConfigureAwait(false);
        buffer.Position = 0;
    }
}
