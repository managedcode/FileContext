namespace ManagedCode.FileContext;

public sealed record FileContextSpreadsheetCell(string? Text = null, double? Number = null, bool? Boolean = null, string? Formula = null);
