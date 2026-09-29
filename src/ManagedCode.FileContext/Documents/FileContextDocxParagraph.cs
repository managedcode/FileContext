namespace ManagedCode.FileContext;

/// <summary>One paragraph or a segment of a long paragraph; character offsets are zero-based.</summary>
public sealed record FileContextDocxParagraph(int Number, int CharacterOffset, string Text);
