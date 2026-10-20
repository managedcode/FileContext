namespace ManagedCode.FileContext;

public sealed record FileContextWorkbook(IReadOnlyList<FileContextWorksheet> Sheets)
{
    /// <summary>Optional existing .xlsx in this exact authorized file scope.</summary>
    public string? TemplatePath { get; init; }
}
