namespace ManagedCode.FileContext;

/// <summary>Formula is source text only. Value is the saved cached result and can be empty; formulas are never evaluated.</summary>
public sealed record FileContextWorkbookCell(string Address, string Type, string Value, string? Formula);
