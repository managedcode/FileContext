using DocumentFormat.OpenXml.Packaging;

namespace ManagedCode.FileContext;

public sealed partial class FileContextDocumentService
{
    public Task<FileContextWorkbookInfo> GetWorkbookInfoAsync(string path, CancellationToken cancellationToken = default) =>
        ReadWorkbookAsync(path, (document, _) => FileContextWorkbookReader.Inspect(document, path), cancellationToken);

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
        return FileContextOperation.RunAsync(options.OperationTimeout, async token =>
        {
            var metadata = await store.GetMetadataAsync(path, token).ConfigureAwait(false)
                ?? throw new FileNotFoundException("The workbook was not found in this file context.", path);
            if (metadata.Length > (ulong)options.MaximumFullReadBytes)
            { throw new IOException("Workbook exceeds the configured source read budget."); }
            var source = await store.OpenReadAsync(path, token).ConfigureAwait(false);
            await using var lifetime = source.ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > options.MaximumFullReadBytes)
                { throw new IOException("Workbook exceeds the configured source read budget."); }
                buffer.Write(chunk, 0, count);
            }
            buffer.Position = 0;
            token.ThrowIfCancellationRequested();
            using var document = SpreadsheetDocument.Open(buffer, false);
            return read(document, token);
        }, cancellationToken);
    }
}
