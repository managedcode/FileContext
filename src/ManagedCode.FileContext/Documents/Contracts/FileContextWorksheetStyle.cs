namespace ManagedCode.FileContext;

/// <summary>Column dictionaries use one-based Excel column numbers. Number formats use Excel format codes.</summary>
public sealed record FileContextWorksheetStyle
{
    public bool BoldHeader { get; init; }
    public string? HeaderFill { get; init; }
    public bool FreezeHeader { get; init; }
    public IReadOnlyDictionary<int, string> NumberFormats { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, double> ColumnWidths { get; init; } = new Dictionary<int, double>();
}
