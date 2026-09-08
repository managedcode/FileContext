namespace ManagedCode.FileContext;

public sealed record FileContextCsvDocument(IReadOnlyList<IReadOnlyList<string?>> Rows, string Delimiter = ",");
