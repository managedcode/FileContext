namespace ManagedCode.FileContext;

/// <summary>A ranked Markdown knowledge-graph search result.</summary>
public sealed record MarkdownGraphSearchResult(
    int DocumentCount,
    int TripleCount,
    IReadOnlyList<MarkdownGraphMatch> Matches);
