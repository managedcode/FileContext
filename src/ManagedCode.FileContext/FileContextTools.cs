using System.ComponentModel;

namespace ManagedCode.FileContext;

internal sealed class FileContextTools(IFileContext fileContext)
{
    [Description(FileContextToolDescriptions.ReadRange)]
    public Task<FileContextRange> ReadRangeAsync(
        [Description(FileContextToolDescriptions.RelativeFilePath)] string path,
        [Description(FileContextToolDescriptions.StartLine)] int startLine = FileContextDefaults.FirstLineNumber,
        [Description(FileContextToolDescriptions.LineCount)] int? lineCount = null,
        CancellationToken cancellationToken = default)
    {
        return fileContext.ReadRangeAsync(path, startLine, lineCount, cancellationToken);
    }

    [Description(FileContextToolDescriptions.GetInfo)]
    public Task<FileContextInfo?> GetInfoAsync(
        [Description(FileContextToolDescriptions.RelativeFilePath)] string path,
        CancellationToken cancellationToken = default)
    {
        return fileContext.GetInfoAsync(path, cancellationToken);
    }

    [Description(FileContextToolDescriptions.SearchMarkdownGraph)]
    public Task<MarkdownGraphSearchResult> SearchMarkdownGraphAsync(
        [Description(FileContextToolDescriptions.GraphQuery)] string query,
        [Description(FileContextToolDescriptions.OptionalMarkdownDirectory)] string directory = "",
        CancellationToken cancellationToken = default)
    {
        return fileContext.SearchMarkdownGraphAsync(query, directory, cancellationToken);
    }

    [Description(FileContextToolDescriptions.ExportMarkdownGraph)]
    public Task<MarkdownGraphExportResult> ExportMarkdownGraphAsync(
        [Description(FileContextToolDescriptions.GraphFormat)] MarkdownGraphFormat format,
        [Description(FileContextToolDescriptions.OptionalMarkdownDirectory)] string directory = "",
        CancellationToken cancellationToken = default)
    {
        return fileContext.ExportMarkdownGraphAsync(format, directory, cancellationToken);
    }
}
