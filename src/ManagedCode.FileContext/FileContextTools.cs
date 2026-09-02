using System.ComponentModel;

namespace ManagedCode.FileContext;

internal sealed class FileContextTools(IFileContext fileContext)
{
    [Description("Read a bounded, one-based line range from a text file. Use this instead of a full read for large files.")]
    public Task<FileContextRange> ReadRangeAsync(
        [Description("Relative slash-separated file path.")] string path,
        [Description("One-based first line to read.")] int startLine = 1,
        [Description("Number of lines to return; omitted uses the configured default.")] int? lineCount = null,
        CancellationToken cancellationToken = default)
    {
        return fileContext.ReadRangeAsync(path, startLine, lineCount, cancellationToken);
    }

    [Description("Return file size, media type, and last-modified time without reading its content.")]
    public Task<FileContextInfo?> GetInfoAsync(
        [Description("Relative slash-separated file path.")] string path,
        CancellationToken cancellationToken = default)
    {
        return fileContext.GetInfoAsync(path, cancellationToken);
    }

    [Description("Build a linked-data knowledge graph from scoped Markdown files and search its concepts and relationships.")]
    public Task<MarkdownGraphSearchResult> SearchMarkdownGraphAsync(
        [Description("Concept or relationship query.")] string query,
        [Description("Optional relative directory containing Markdown files.")] string directory = "",
        CancellationToken cancellationToken = default)
    {
        return fileContext.SearchMarkdownGraphAsync(query, directory, cancellationToken);
    }

    [Description("Build a linked-data graph from scoped Markdown files and export it as Mermaid, DOT, Turtle, or JSON-LD.")]
    public Task<MarkdownGraphExportResult> ExportMarkdownGraphAsync(
        [Description("Export format: Mermaid, Dot, Turtle, or JsonLd.")] MarkdownGraphFormat format,
        [Description("Optional relative directory containing Markdown files.")] string directory = "",
        CancellationToken cancellationToken = default)
    {
        return fileContext.ExportMarkdownGraphAsync(format, directory, cancellationToken);
    }
}
