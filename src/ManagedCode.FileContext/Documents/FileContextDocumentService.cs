using System.Text;

namespace ManagedCode.FileContext;

/// <summary>Creates new documents in the same scoped file store used by navigation and reads.</summary>
public sealed class FileContextDocumentService(ManagedCodeStorageFileStore store, FileContextOptions options)
{
    public Task<FileContextCreatedFile> CreateTextAsync(string fileName, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return CreateAsync(fileName, "text/plain; charset=utf-8", _ => Encoding.UTF8.GetBytes(text), cancellationToken);
    }

    public Task<FileContextCreatedFile> CreateCsvAsync(string fileName, FileContextCsvDocument document, CancellationToken cancellationToken = default) =>
        CreateAsync(RequireExtension(fileName, ".csv"), "text/csv; charset=utf-8",
            token => FileContextCsvWriter.Create(document, token), cancellationToken);

    public Task<FileContextCreatedFile> CreateWorkbookAsync(string fileName, FileContextWorkbook workbook, CancellationToken cancellationToken = default) =>
        CreateAsync(RequireExtension(fileName, ".xlsx"), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            token => FileContextWorkbookWriter.Create(workbook, token), cancellationToken);

    public Task<FileContextCreatedFile> CreatePdfAsync(string fileName, FileContextPdfDocument document, CancellationToken cancellationToken = default) =>
        CreateAsync(RequireExtension(fileName, ".pdf"), "application/pdf",
            token => FileContextPdfWriter.Create(document, token), cancellationToken);

    private Task<FileContextCreatedFile> CreateAsync(string fileName, string mediaType,
        Func<CancellationToken, byte[]> generate, CancellationToken cancellationToken)
    {
        options.Validate();
        if (!options.EnableWriteTools) { throw new InvalidOperationException("Document creation requires EnableWriteTools."); }
        var normalized = StoragePathScope.Normalize(fileName);
        return FileContextOperation.RunAsync(options.OperationTimeout, async token =>
        {
            var bytes = generate(token);
            using var content = new MemoryStream(bytes, writable: false);
            return await store.CreateFileAsync(normalized, content, mediaType, options.MaximumGeneratedFileBytes, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    private static string RequireExtension(string path, string extension)
    {
        StoragePathScope.Normalize(path);
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The file name must end in {extension}.", nameof(path));
        }
        return path;
    }
}
