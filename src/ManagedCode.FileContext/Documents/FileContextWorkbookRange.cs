namespace ManagedCode.FileContext;

/// <summary>A sparse rectangular range. Coordinates absent from Cells are blank; values are stored values, not formatted display text.</summary>
public sealed record FileContextWorkbookRange(string Path, string Sheet, string Range, IReadOnlyList<FileContextWorkbookCell> Cells);
