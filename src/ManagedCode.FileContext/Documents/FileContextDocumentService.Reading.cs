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

    private Task<T> ReadSourceAsync<T>(string path, Func<MemoryStream, CancellationToken, T> read,
        long maximumBytes, CancellationToken cancellationToken) =>
        FileContextOperation.RunAsync(options.OperationTimeout, async token =>
        {
            using var buffer = new MemoryStream();
            await CopyScopedSourceAsync(path, buffer, maximumBytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return read(buffer, token);
        }, cancellationToken);

    private async Task CopyScopedSourceAsync(string path, MemoryStream buffer, long maximumBytes, CancellationToken token)
    {
        var metadata = await store.GetMetadataAsync(path, token).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The document was not found in this file context.", path);
        if (metadata.Length > (ulong)maximumBytes)
        { throw new IOException("Document exceeds the configured source read budget."); }
        var source = await store.OpenReadInOperationAsync(path, token).ConfigureAwait(false);
        await using var lifetime = source.ConfigureAwait(false);
        using var bounded = new StorageReadBudgetStream(source, maximumBytes);
        await bounded.CopyToAsync(buffer, StorageReadBudgetStream.DefaultCopyBufferSize, token).ConfigureAwait(false);
        buffer.Position = 0;
    }
}
