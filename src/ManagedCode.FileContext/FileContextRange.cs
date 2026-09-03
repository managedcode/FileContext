namespace ManagedCode.FileContext;

/// <summary>A bounded line window read from a text file.</summary>
public sealed record FileContextRange(
    string Path,
    int StartLine,
    int EndLine,
    int? TotalLines,
    bool HasMore,
    string Content);
