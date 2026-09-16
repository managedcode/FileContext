namespace ManagedCode.FileContext;

/// <summary>Headers preserve order, blanks and duplicates. HeaderRow is a one-based worksheet row or CSV logical record.</summary>
public sealed record FileContextTableInfo(string Name, string? Sheet, int? HeaderRow, int StartColumn,
    IReadOnlyList<string> Headers, long RowCount);
