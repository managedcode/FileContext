namespace ManagedCode.FileContext;

/// <summary>A bounded window of Word paragraphs. Continue from the returned cursor when present.</summary>
public sealed record FileContextDocxText(
    string Path,
    IReadOnlyList<FileContextDocxParagraph> Paragraphs,
    int? NextParagraph,
    int NextCharacter);
