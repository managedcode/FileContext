namespace ManagedCode.FileContext;

public sealed record FileContextWorksheet(string Name, IReadOnlyList<IReadOnlyList<FileContextSpreadsheetCell>> Rows)
{
    public FileContextWorksheetStyle? Style { get; init; }
}
