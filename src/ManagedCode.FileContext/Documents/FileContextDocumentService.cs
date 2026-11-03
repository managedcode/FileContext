using System.Text;

namespace ManagedCode.FileContext;

/// <summary>Creates new documents in the same scoped file store used by navigation and reads.</summary>
public sealed partial class FileContextDocumentService(ManagedCodeStorageFileStore store, FileContextOptions options)
{
    public Task<FileContextCreatedFile> CreateTextAsync(string fileName, string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return CreateAsync(fileName, "text/plain; charset=utf-8", (limit, _) =>
        {
            if (limit != long.MaxValue)
            {
                StorageFileCreator.EnsureWithinLimit(Encoding.UTF8.GetByteCount(text), limit);
            }
            return new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false);
        }, cancellationToken);
    }

    public Task<FileContextCreatedFile> CreateCsvAsync(string fileName, FileContextCsvDocument document, CancellationToken cancellationToken = default) =>
        CreateAsync(RequireExtension(fileName, ".csv"), "text/csv; charset=utf-8",
            (limit, token) => FileContextCsvWriter.Create(document, limit, token), cancellationToken);

    public async Task<FileContextCreatedFile> CreateWorkbookAsync(string fileName, FileContextWorkbook workbook, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        options.Validate();
        if (!options.EnableWriteTools) { throw new InvalidOperationException("Document creation requires EnableWriteTools."); }
        var name = RequireExtension(fileName, ".xlsx");
        var template = workbook.TemplatePath is { } path ? RequireExtension(path, ".xlsx") : null;
        return await FileContextOperation.RunAsync(options.OperationTimeout, async token =>
        {
            using var buffer = new MemoryStream();
            if (template is not null)
            {
                await CopyScopedSourceAsync(template, buffer, options.MaximumFullReadBytes, token).ConfigureAwait(false);
            }
            FileContextWorkbookWriter.Write(workbook, buffer, template is not null, options.MaximumOpenXmlPartCharacters, token);
            return await store.CreatePreparedFileAsync(name, buffer, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                options.MaximumGeneratedFileBytes, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<FileContextCreatedFile> CreatePdfAsync(string fileName, FileContextPdfDocument document, CancellationToken cancellationToken = default) =>
        CreateAsync(RequireExtension(fileName, ".pdf"), "application/pdf",
            (_, token) => new MemoryStream(FileContextPdfWriter.Create(document, token), writable: false), cancellationToken);

    private Task<FileContextCreatedFile> CreateAsync(string fileName, string mediaType,
        Func<long, CancellationToken, MemoryStream> generate, CancellationToken cancellationToken)
    {
        options.Validate();
        if (!options.EnableWriteTools) { throw new InvalidOperationException("Document creation requires EnableWriteTools."); }
        var normalized = StoragePathScope.Normalize(fileName);
        return FileContextOperation.RunAsync(options.OperationTimeout, async token =>
        {
            var limit = store.GetGeneratedFileLimit(options.MaximumGeneratedFileBytes);
            using var content = generate(limit, token);
            return await store.CreatePreparedFileAsync(normalized, content, mediaType, limit, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    private static string RequireExtension(string path, string extension)
    {
        var normalized = StoragePathScope.Normalize(path);
        if (!normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The file name must end in {extension}.", nameof(path));
        }
        return normalized;
    }
    /// <summary>Renders all outputs before storing any file in the scoped FileContext.</summary>
    public async Task<FileContextReportResult> CreateReportAsync(FileContextReport report,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        if (!options.EnableWriteTools) { throw new InvalidOperationException("Document creation requires EnableWriteTools."); }
        var limit = store.GetGeneratedFileLimit(options.MaximumGeneratedFileBytes);
        return await FileContextOperation.RunAsync(options.OperationTimeout, async token =>
        {
            var outputs = FileContextReportRenderer.Render(report, options, limit, token);
            var files = new List<FileContextCreatedFile>(outputs.Count);
            foreach (var output in outputs)
            {
                using var content = new MemoryStream(output.Bytes, writable: false);
                files.Add(await store.CreatePreparedFileAsync(output.FileName, content, output.ContentType,
                    limit, token).ConfigureAwait(false));
            }
            return new FileContextReportResult(files);
        }, cancellationToken).ConfigureAwait(false);
    }

}
