namespace ManagedCode.FileContext;

/// <summary>One ranked Markdown knowledge-graph match.</summary>
public sealed record MarkdownGraphMatch(
    string NodeId,
    string Label,
    string? Description,
    string Source,
    double Score);
