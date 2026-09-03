namespace ManagedCode.FileContext;

/// <summary>A bounded serialized Markdown knowledge graph.</summary>
public sealed record MarkdownGraphExportResult(
    MarkdownGraphFormat Format,
    int DocumentCount,
    int TripleCount,
    bool Truncated,
    string Content);
