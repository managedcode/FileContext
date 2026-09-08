namespace ManagedCode.FileContext;

public sealed record FileContextPdfDocument(IReadOnlyList<string> Paragraphs, string? Title = null);
