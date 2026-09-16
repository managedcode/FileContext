namespace ManagedCode.FileContext;

/// <summary>Compact metadata only; row counts exclude headers, totals and wholly empty records.</summary>
public sealed record FileContextTablesInfo(string Path, string Format, string? Delimiter, IReadOnlyList<FileContextTableInfo> Tables);

