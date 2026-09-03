namespace ManagedCode.FileContext;

/// <summary>Provides bounded file navigation, metadata, and Markdown graph operations.</summary>
public interface IFileContext
{
    Task<FileContextRange> ReadRangeAsync(
        string path,
        int startLine = FileContextDefaults.FirstLineNumber,
        int? lineCount = null,
        CancellationToken cancellationToken = default);

    Task<FileContextInfo?> GetInfoAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<MarkdownGraphSearchResult> SearchMarkdownGraphAsync(
        string query,
        string directory = "",
        CancellationToken cancellationToken = default);

    Task<MarkdownGraphExportResult> ExportMarkdownGraphAsync(
        MarkdownGraphFormat format,
        string directory = "",
        CancellationToken cancellationToken = default);
}
