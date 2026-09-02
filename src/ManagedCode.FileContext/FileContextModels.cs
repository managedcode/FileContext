namespace ManagedCode.FileContext;

/// <summary>A bounded line window read from a text file.</summary>
public sealed record FileContextRange(
    string Path,
    int StartLine,
    int EndLine,
    int? TotalLines,
    bool HasMore,
    string Content);

/// <summary>Provider-neutral metadata for a file.</summary>
public sealed record FileContextInfo(
    string Path,
    ulong Length,
    string? ContentType,
    DateTimeOffset LastModified);

/// <summary>One ranked Markdown knowledge-graph match.</summary>
public sealed record MarkdownGraphMatch(
    string NodeId,
    string Label,
    string? Description,
    string Source,
    double Score);

/// <summary>A ranked Markdown knowledge-graph search result.</summary>
public sealed record MarkdownGraphSearchResult(
    int DocumentCount,
    int TripleCount,
    IReadOnlyList<MarkdownGraphMatch> Matches);

/// <summary>Supported linked-data and diagram export formats.</summary>
public enum MarkdownGraphFormat
{
    Mermaid,
    Dot,
    Turtle,
    JsonLd,
}

/// <summary>A bounded serialized Markdown knowledge graph.</summary>
public sealed record MarkdownGraphExportResult(
    MarkdownGraphFormat Format,
    int DocumentCount,
    int TripleCount,
    bool Truncated,
    string Content);
