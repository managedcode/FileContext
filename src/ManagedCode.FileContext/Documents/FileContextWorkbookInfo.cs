namespace ManagedCode.FileContext;

public sealed record FileContextWorkbookInfo(string Path, IReadOnlyList<FileContextWorksheetInfo> Sheets);
